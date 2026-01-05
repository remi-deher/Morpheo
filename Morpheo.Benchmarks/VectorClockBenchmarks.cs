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
            // Local clock has value i
            _masterLocalClock[nodeId] = i;
            
            // Remote clock has higher value (i + 10) to force updates during Merge
            _remoteClock[nodeId] = i + 10;
        }

        // Initialize _localClock for the first run or for CompareTo which doesn't modify it
        _localClock = new VectorClock(_masterLocalClock);
    }

    [IterationSetup(Target = nameof(Merge))]
    public void IterationSetup()
    {
        // Reset local clock before each Merge iteration because Merge is in-place
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
        // CompareTo is read-only, so no need for IterationSetup overhead
        return _localClock.CompareTo(_remoteClock);
    }
}
