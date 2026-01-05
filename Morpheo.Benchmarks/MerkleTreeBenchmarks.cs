using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Morpheo.Core.Sync;
using Morpheo.Core.Data;
using Morpheo.Sdk;

namespace Morpheo.Benchmarks;

[MarkdownExporterAttribute.GitHub]
[MemoryDiagnoser]
public class MerkleTreeBenchmarks
{
    private MerkleTreeService _merkleService = null!;
    private List<SyncLogDto> _logs = null!;

    [Params(1000, 10000)]
    public int N;

    [GlobalSetup]
    public void Setup()
    {
        _merkleService = new MerkleTreeService();
        _logs = new List<SyncLogDto>(N);

        for (int i = 0; i < N; i++)
        {
            _logs.Add(new SyncLogDto(
                Guid.NewGuid().ToString(),
                "test-entity",
                "App.User",
                "{\"key\": \"value\"}",
                "UPDATE",
                DateTime.UtcNow.Ticks,
                new Dictionary<string, long>(),
                "node-A"
            ));
        }
    }

    [Benchmark]
    public string CalculateRootHash()
    {
        // Project SyncLogDto to string (e.g. JsonData) because ComputeRootHash expects strings
        return _merkleService.ComputeRootHash(_logs.Select(l => l.JsonData));
    }
}
