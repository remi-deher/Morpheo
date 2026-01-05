using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Morpheo.Core.Data;
using Morpheo.Core.Sync;
using Morpheo.Sdk;
using System.Text.Json;

namespace Morpheo.Benchmarks;

[MarkdownExporterAttribute.GitHub]
[MemoryDiagnoser]
public class LogStoreBenchmarks
{
    private FileLogStore _fileStore = null!;
    private SqlSyncLogStore _sqlStore = null!;
    private MorpheoDbContext _dbContext = null!;
    private string _tempPath = null!;

    private List<SyncLogDto> _logsToWrite = null!;

    [Params(100, 1000)]
    public int N;

    [GlobalSetup]
    public void Setup()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "Morpheo_Bench_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempPath);

        _logsToWrite = new List<SyncLogDto>(N);
        for (int i = 0; i < N; i++)
        {
            _logsToWrite.Add(new SyncLogDto(
                Id: Guid.NewGuid().ToString(),
                EntityId: $"doc_{i}",
                EntityName: "BenchmarkEntity",
                JsonData: JsonSerializer.Serialize(new { Value = i, Payload = "BenchmarkData" }),
                Action: "UPDATE",
                Timestamp: DateTime.UtcNow.Ticks,
                VectorClock: new Dictionary<string, long>(),
                OriginNodeId: "Node-Benchmark"
            ));
        }

        _fileStore = new FileLogStore(_tempPath);
        _fileStore.StartAsync().GetAwaiter().GetResult();

        var dbPath = Path.Combine(_tempPath, "bench.db");
        var dbOptions = new DbContextOptionsBuilder<MorpheoDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        _dbContext = new MorpheoDbContext(dbOptions);
        _dbContext.Database.EnsureCreated();

        _sqlStore = new SqlSyncLogStore(_dbContext);
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
    public async Task Write_SQLite_EF()
    {
        foreach (var logDto in _logsToWrite)
        {
            var syncLog = new SyncLog
            {
                Id = logDto.Id,
                EntityId = logDto.EntityId,
                EntityName = logDto.EntityName,
                JsonData = logDto.JsonData,
                Action = logDto.Action,
                Timestamp = logDto.Timestamp,
                VectorClockJson = JsonSerializer.Serialize(logDto.VectorClock),
                IsFromRemote = false
            };
            await _sqlStore.AddLogAsync(syncLog);
        }
    }

    [Benchmark]
    public async Task Write_FileStore_LSM()
    {
        foreach (var log in _logsToWrite)
        {
            await _fileStore.AddLogAsync(log);
        }
    }
}