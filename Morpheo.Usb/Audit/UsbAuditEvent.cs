namespace Morpheo.Usb.Audit;

/// <summary>An immutable record of one USB device access event.</summary>
public class UsbAuditEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string PrincipalId { get; set; }
    public required string DeviceId { get; set; }
    public required string Operation { get; set; }  // "List", "Read", "Write", "Delete", "Print"
    public required UsbAccessType AccessType { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long? BytesTransferred { get; set; }
    public TimeSpan? Duration { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? ClientIp { get; set; }
}
