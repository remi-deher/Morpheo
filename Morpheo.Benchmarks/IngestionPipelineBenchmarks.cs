using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Morpheo.Core.Data;
using Morpheo.Core.Sync;
using Morpheo.Sdk;
using System.Text.Json;

namespace Morpheo.Benchmarks;

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class IngestionPipelineBenchmarks
{
    private FileLogStore _fileStore = null!;
    private SqlSyncLogStore _sqlStore = null!;
    private MorpheoDbContext _dbContext = null!;
    private MerkleTreeService _merkleService = null!;
    private string _tempPath = null!;
    private SyncLogDto _incomingLog = null!;
    private VectorClock _localCheckClock = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "Morpheo_Pipeline_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempPath);

        _merkleService = new MerkleTreeService();
        _localCheckClock = new VectorClock();
        _localCheckClock["NodeA"] = 100;

        _fileStore = new FileLogStore(_tempPath);
        _fileStore.StartAsync().GetAwaiter().GetResult();

        var dbPath = Path.Combine(_tempPath, "pipeline.db");
        var dbOptions = new DbContextOptionsBuilder<MorpheoDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        
        _dbContext = new MorpheoDbContext(dbOptions);
        _dbContext.Database.EnsureCreated();
        
        _sqlStore = new SqlSyncLogStore(_dbContext);

        _incomingLog = new SyncLogDto(
            Id: Guid.NewGuid().ToString(),
            EntityId: "doc_X509",
            EntityName: "Order",
            JsonData: "{\"Amount\": 150.00, \"Currency\": \"EUR\", \"Items\": [\"A\", \"B\"]}",
            Action: "CREATE",
            Timestamp: DateTime.UtcNow.Ticks,
            VectorClock: new Dictionary<string, long> { { "NodeB", 201 } },
            OriginNodeId: "NodeB"
        );
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _fileStore?.StopAsync().GetAwaiter().GetResult();
        _dbContext?.Dispose();
        
        if (Directory.Exists(_tempPath))
        {
            try { Directory.Delete(_tempPath, recursive: true); } catch { }
        }
    }

    [Benchmark(Baseline = true)]
    public async Task Pipeline_Standard_SQL()
    {
        var entity = new SyncLog
        {
            Id = Guid.NewGuid().ToString(), 
            EntityId = _incomingLog.EntityId,
            EntityName = _incomingLog.EntityName,
            JsonData = _incomingLog.JsonData,
            Action = _incomingLog.Action,
            Timestamp = _incomingLog.Timestamp,
            VectorClockJson = JsonSerializer.Serialize(_incomingLog.VectorClock),
            IsFromRemote = true
        };

        await _sqlStore.AddLogAsync(entity);
    }

    [Benchmark]
    public async Task Pipeline_Morpheo_LSM()
    {
        var remoteClock = new VectorClock(_incomingLog.VectorClock);
        _localCheckClock.Merge(remoteClock);

        var hash = _merkleService.ComputeRootHash(new[] { _incomingLog.JsonData });

        await _fileStore.AddLogAsync(_incomingLog);
    }
}
