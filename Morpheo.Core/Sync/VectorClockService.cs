using System.Text.Json;
using Morpheo.Sdk;

namespace Morpheo.Core.Sync;

/// <summary>
/// Service implementation of Vector Clocks.
/// Wraps the original dictionary-based logic behind ILogicalClock.
/// </summary>
public class VectorClockService : ILogicalClock
{
    private Dictionary<string, long> _clock = new();
    private readonly string _localNodeId;
    private readonly object _lock = new();

    public VectorClockService(MorpheoOptions options)
    {
        _localNodeId = options.NodeName;
    }

    public void Increment()
    {
        lock (_lock)
        {
            if (!_clock.ContainsKey(_localNodeId))
                _clock[_localNodeId] = 0;
            _clock[_localNodeId]++;
        }
    }

    public void Merge(string? remoteState)
    {
        if (string.IsNullOrEmpty(remoteState)) return;

        var remoteClock = JsonSerializer.Deserialize<Dictionary<string, long>>(remoteState);
        if (remoteClock == null) return;

        lock (_lock)
        {
            foreach (var pair in remoteClock)
            {
                if (!_clock.TryGetValue(pair.Key, out var currentVal))
                    _clock[pair.Key] = pair.Value;
                else
                    _clock[pair.Key] = Math.Max(currentVal, pair.Value);
            }
        }
    }

    public ClockRelation CompareTo(string? remoteState)
    {
        // Null/Empty remote state is considered "older" or empty, so I cause it.
        if (string.IsNullOrEmpty(remoteState)) return ClockRelation.Causes;

        var remoteClock = JsonSerializer.Deserialize<Dictionary<string, long>>(remoteState);
        if (remoteClock == null) return ClockRelation.Causes;

        bool hasGreater = false;
        bool hasLess = false;
        
        lock (_lock)
        {
            var allKeys = _clock.Keys.Union(remoteClock.Keys);

            foreach (var key in allKeys)
            {
                long myVal = _clock.TryGetValue(key, out var v1) ? v1 : 0;
                long otherVal = remoteClock.TryGetValue(key, out var v2) ? v2 : 0;

                if (myVal > otherVal) hasGreater = true;
                if (myVal < otherVal) hasLess = true;
            }
        }

        if (!hasGreater && !hasLess) return ClockRelation.Equal;
        if (hasGreater && !hasLess) return ClockRelation.Causes;
        if (!hasGreater && hasLess) return ClockRelation.CausedBy;

        return ClockRelation.Concurrent;
    }

    public string Serialize()
    {
        lock (_lock)
        {
            return JsonSerializer.Serialize(_clock);
        }
    }
}
