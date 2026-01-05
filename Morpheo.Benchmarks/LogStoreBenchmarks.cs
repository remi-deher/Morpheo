using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Morpheo.Core.Data;
using Morpheo.Core.Sync;
using Morpheo.Sdk;
using System.Text.Json;

namespace Morpheo.Benchmarks;

[MarkdownExporterAttribute.GitHub] // Génère le tableau Markdown pour votre README
[MemoryDiagnoser]                  // Mesure l'empreinte mémoire (Gen0, Gen1, Allocations)
public class LogStoreBenchmarks
{
    private FileLogStore _fileStore = null!;
    private SqlSyncLogStore _sqlStore = null!;
    private MorpheoDbContext _dbContext = null!;
    private string _tempPath = null!;

    // Données pré-générées pour ne mesurer que l'écriture
    private List<SyncLogDto> _logsToWrite = null!;

    [Params(100, 1000)] // On teste avec 100 et 1000 opérations
    public int N;

    [GlobalSetup]
    public void Setup()
    {
        // 1. Préparation des chemins temporaires
        _tempPath = Path.Combine(Path.GetTempPath(), "Morpheo_Bench_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempPath);

        // 2. Génération des logs factices
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

        // 3. Initialisation du FileLogStore (Votre moteur LSM)
        _fileStore = new FileLogStore(_tempPath);
        _fileStore.StartAsync().GetAwaiter().GetResult();

        // 4. Initialisation du SqlSyncLogStore (Entity Framework + SQLite)
        // Note: On utilise un fichier .db réel pour être équitable sur les I/O disque
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
        // Nettoyage propre des fichiers créés
        if (Directory.Exists(_tempPath))
        {
            try { Directory.Delete(_tempPath, recursive: true); } catch { }
        }
    }

    [Benchmark(Baseline = true)]
    public async Task Write_SQLite_EF()
    {
        // Simule l'écriture via EF Core (Standard)
        foreach (var logDto in _logsToWrite)
        {
            // Conversion manuelle car SqlSyncLogStore attend l'entité interne SyncLog
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
        // Simule l'écriture via votre moteur optimisé (Morpheo)
        foreach (var log in _logsToWrite)
        {
            await _fileStore.AddLogAsync(log);
        }
    }
}