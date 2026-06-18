namespace Morpheo.Usb.Audit;

using Microsoft.Extensions.Logging;

/// <summary>
/// In-memory audit logger (demo). Replace with a DB-backed implementation for production.
/// </summary>
public sealed class UsbAuditLogger : IUsbAuditLogger
{
    private readonly List<UsbAuditEvent> _events = new();
    private readonly ILogger<UsbAuditLogger> _logger;
    private readonly Lock _lock = new();

    public UsbAuditLogger(ILogger<UsbAuditLogger> logger) => _logger = logger;

    public Task LogAsync(UsbAuditEvent @event, CancellationToken ct = default)
    {
        lock (_lock) { _events.Add(@event); }

        _logger.Log(
            @event.Success ? LogLevel.Information : LogLevel.Warning,
            "[USB Audit] {Op} on {Device} by {Principal} — {Result} ({Bytes} bytes, {Duration}ms)",
            @event.Operation, @event.DeviceId, @event.PrincipalId,
            @event.Success ? "OK" : "FAIL",
            @event.BytesTransferred ?? 0,
            (long?)@event.Duration?.TotalMilliseconds ?? 0);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsbAuditEvent>> QueryAsync(
        string? principalId = null,
        string? deviceId = null,
        DateTimeOffset? since = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            IEnumerable<UsbAuditEvent> q = _events;
            if (!string.IsNullOrEmpty(principalId)) q = q.Where(e => e.PrincipalId == principalId);
            if (!string.IsNullOrEmpty(deviceId)) q = q.Where(e => e.DeviceId == deviceId);
            if (since.HasValue) q = q.Where(e => e.Timestamp >= since.Value.UtcDateTime);

            return Task.FromResult<IReadOnlyList<UsbAuditEvent>>(
                q.OrderByDescending(e => e.Timestamp).Take(limit).ToList().AsReadOnly());
        }
    }
}
