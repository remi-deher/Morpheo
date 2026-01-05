using Morpheo.Sdk;

namespace Morpheo.Core.Sync;

/// <summary>
/// Hybrid Logical Clock (HLC) implementation.
/// Combines physical time with a logical counter to provide causality 
/// while keeping time close to NTP.
/// </summary>
public class HybridClockService : ILogicalClock
{
    private long _physicalTime; // pt
    private int _logicalCounter; // l
    private readonly object _lock = new();

    // Max drift allowed (e.g. 1 minute)
    private const long MaxDriftMs = 60000;

    public HybridClockService()
    {
        _physicalTime = GetPhysicalTime();
        _logicalCounter = 0;
    }

    public void Increment()
    {
        lock (_lock)
        {
            long now = GetPhysicalTime();
            if (now > _physicalTime)
            {
                _physicalTime = now;
                _logicalCounter = 0;
            }
            else
            {
                // Physical time hasn't moved (or went back), invoke logical tick
                _logicalCounter++;
            }
        }
    }

    public void Merge(string? remoteState)
    {
        if (string.IsNullOrEmpty(remoteState)) return;
        
        var (remotePt, remoteLc) = ParseHlc(remoteState);
        long now = GetPhysicalTime();

        // Check for excessive drift (future messages)
        if (remotePt > now + MaxDriftMs)
        {
            // We could reject, but for now we just log/ignore or cap?
            // HLC paper suggests ignoring if it exceeds too much, 
            // but in sync we might just accept.
            // Let's proceed with standard HLC merge rules.
        }

        lock (_lock)
        {
            long oldPt = _physicalTime;
            
            _physicalTime = Math.Max(oldPt, Math.Max(remotePt, now));

            if (_physicalTime == oldPt && _physicalTime == remotePt)
            {
                _logicalCounter = Math.Max(_logicalCounter, remoteLc) + 1;
            }
            else if (_physicalTime == oldPt)
            {
                _logicalCounter++;
            }
            else if (_physicalTime == remotePt)
            {
                _logicalCounter = remoteLc + 1;
            }
            else
            {
                _logicalCounter = 0;
            }
        }
    }

    public ClockRelation CompareTo(string? remoteState)
    {
        if (string.IsNullOrEmpty(remoteState)) return ClockRelation.Causes;

        var (remotePt, remoteLc) = ParseHlc(remoteState);

        // HLC Comparison is lexicographical on (pt, lc)
        // If (pt1, lc1) > (pt2, lc2) => Causes
        
        lock (_lock)
        {
            if (_physicalTime > remotePt) return ClockRelation.Causes;
            if (_physicalTime < remotePt) return ClockRelation.CausedBy;

            // Physical times equal, check logical
            if (_logicalCounter > remoteLc) return ClockRelation.Causes;
            if (_logicalCounter < remoteLc) return ClockRelation.CausedBy;

            return ClockRelation.Equal;
        }
    }

    public string Serialize()
    {
        lock (_lock)
        {
            return $"{_physicalTime}:{_logicalCounter}";
        }
    }

    private long GetPhysicalTime()
    {
         return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private (long pt, int lc) ParseHlc(string hlcString)
    {
        var parts = hlcString.Split(':');
        if (parts.Length != 2) return (0, 0);

        if (long.TryParse(parts[0], out var pt) && int.TryParse(parts[1], out var lc))
            return (pt, lc);

        return (0, 0);
    }
}
