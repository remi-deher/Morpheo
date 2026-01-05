using System;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Morpheo.Core.Sync;

namespace Morpheo.Benchmarks;


public class DeltaCompressionBenchmarks
{
    private DeltaCompressionService _deltaService;
    private string _sourceJson;
    private string _targetJson;
    private string _patch;

    [GlobalSetup]
    public void Setup()
    {
        _deltaService = new DeltaCompressionService();

        // Create large JSONs (approx 100KB)
        var sourceObj = Enumerable.Range(0, 5000)
            .Select(i => new { Id = i, Data = Guid.NewGuid().ToString() })
            .ToList();

        var targetObj = sourceObj.ToList();
        // Modify some items to create differences
        targetObj[100] = new { Id = 100, Data = "Modified Data Here" };
        targetObj[2500] = new { Id = 2500, Data = "Another Modification" };
        targetObj.Add(new { Id = 5001, Data = "New Item" });

        _sourceJson = System.Text.Json.JsonSerializer.Serialize(sourceObj);
        _targetJson = System.Text.Json.JsonSerializer.Serialize(targetObj);

        // Pre-compute patch for the Apply benchmark
        _patch = _deltaService.CreatePatch(_sourceJson, _targetJson);
    }

    [Benchmark]
    public string ComputeDiff()
    {
        return _deltaService.CreatePatch(_sourceJson, _targetJson);
    }

    [Benchmark]
    public string ApplyPatch()
    {
        return _deltaService.ApplyPatch(_sourceJson, _patch);
    }
}
