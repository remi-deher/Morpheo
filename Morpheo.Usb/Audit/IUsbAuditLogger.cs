namespace Morpheo.Usb.Audit;

public interface IUsbAuditLogger
{
    Task LogAsync(UsbAuditEvent @event, CancellationToken ct = default);

    Task<IReadOnlyList<UsbAuditEvent>> QueryAsync(
        string? principalId = null,
        string? deviceId = null,
        DateTimeOffset? since = null,
        int limit = 200,
        CancellationToken ct = default);
}
