using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Morpheo.Core.Sync;

namespace Morpheo.Benchmarks;

[MemoryDiagnoser]
public class VectorClockBenchmarks
{
    [Params(10, 100, 1000)]
    public int NodeCount;

    private VectorClock _masterLocalClock = null!;
    private VectorClock _localClock = null!;
    private VectorClock _remoteClock = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _masterLocalClock = new VectorClock();
        _remoteClock = new VectorClock();

        for (int i = 0; i < NodeCount; i++)
        {
            string nodeId = $"Node_{i}";
            _masterLocalClock[nodeId] = i;
            
            _remoteClock[nodeId] = i + 10;
        }

        _localClock = new VectorClock(_masterLocalClock);
    }

    [IterationSetup(Target = nameof(Merge))]
    public void IterationSetup()
    {
        _localClock = new VectorClock(_masterLocalClock);
    }

    [Benchmark]
    public void Merge()
    {
        _localClock.Merge(_remoteClock);
    }

    [Benchmark]
    public VectorRelation CompareTo()
    {
        return _localClock.CompareTo(_remoteClock);
    }
}
