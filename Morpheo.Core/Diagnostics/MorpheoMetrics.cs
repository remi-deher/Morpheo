using System.Diagnostics.Metrics;

namespace Morpheo.Core.Diagnostics;

/// <summary>
/// Centralised metric instruments for a Morpheo node, built on the BCL
/// <see cref="System.Diagnostics.Metrics"/> API. Any OpenTelemetry exporter (or
/// <c>dotnet-counters</c>) can subscribe to the meter named <see cref="MeterName"/>
/// without Morpheo taking a dependency on the OpenTelemetry SDK.
///
/// <para>
/// To export with OpenTelemetry, the host app simply does:
/// <code>
/// services.AddOpenTelemetry().WithMetrics(m => m.AddMeter(MorpheoMetrics.MeterName));
/// </code>
/// </para>
/// </summary>
public sealed class MorpheoMetrics : IDisposable
{
    /// <summary>The meter name to subscribe to from an OpenTelemetry pipeline.</summary>
    public const string MeterName = "Morpheo";

    private readonly Meter _meter;

    private readonly Counter<long> _logsBroadcast;
    private readonly Counter<long> _logsReceived;
    private readonly Counter<long> _logsApplied;
    private readonly Counter<long> _conflictsResolved;
    private readonly Counter<long> _coldSyncLogs;
    private readonly Histogram<double> _coldSyncDurationMs;

    private long _peerCount;

    public MorpheoMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _logsBroadcast = _meter.CreateCounter<long>("morpheo.logs.broadcast", unit: "{log}", description: "Local changes broadcast to the network.");
        _logsReceived = _meter.CreateCounter<long>("morpheo.logs.received", unit: "{log}", description: "Remote logs received from peers.");
        _logsApplied = _meter.CreateCounter<long>("morpheo.logs.applied", unit: "{log}", description: "Remote logs accepted and persisted locally.");
        _conflictsResolved = _meter.CreateCounter<long>("morpheo.conflicts.resolved", unit: "{conflict}", description: "Concurrent updates resolved by the conflict engine.");
        _coldSyncLogs = _meter.CreateCounter<long>("morpheo.coldsync.logs", unit: "{log}", description: "Logs pulled during Cold Sync catch-up.");
        _coldSyncDurationMs = _meter.CreateHistogram<double>("morpheo.coldsync.duration", unit: "ms", description: "Wall-clock duration of a Cold Sync session.");

        _meter.CreateObservableGauge("morpheo.peers.count", () => Interlocked.Read(ref _peerCount), unit: "{peer}", description: "Currently known peers.");
    }

    public void RecordBroadcast() => _logsBroadcast.Add(1);
    public void RecordReceived() => _logsReceived.Add(1);
    public void RecordApplied() => _logsApplied.Add(1);
    public void RecordConflictResolved() => _conflictsResolved.Add(1);
    public void RecordColdSync(int logCount, double durationMs)
    {
        _coldSyncLogs.Add(logCount);
        _coldSyncDurationMs.Record(durationMs);
    }

    public void SetPeerCount(long count) => Interlocked.Exchange(ref _peerCount, count);

    public void Dispose() => _meter.Dispose();
}
