namespace Morpheo.Usb;

/// <summary>
/// Low-level USB device I/O. Implemented separately for each platform and for mocking.
/// </summary>
public interface IUsbDriver
{
    // ── Storage I/O ──────────────────────────────────────────────────────────
    /// <summary>Lists all file entries on a storage device (names + sizes).</summary>
    Task<IReadOnlyList<UsbFileEntry>> ListFilesAsync(string deviceId, CancellationToken ct = default);

    /// <summary>Opens a read stream for a file on the device. Returns null if not found.</summary>
    Task<Stream?> ReadFileAsync(string deviceId, string filePath, CancellationToken ct = default);

    /// <summary>Writes (creates or replaces) a file on the device. Returns bytes written.</summary>
    Task<long> WriteFileAsync(string deviceId, string filePath, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Deletes a file from the device. No-op if the file does not exist.</summary>
    Task DeleteFileAsync(string deviceId, string filePath, CancellationToken ct = default);

    // ── Printing ─────────────────────────────────────────────────────────────
    /// <summary>Sends a RAW print job byte array to the device's printer endpoint.</summary>
    Task SendPrintJobAsync(string deviceId, byte[] data, CancellationToken ct = default);

    /// <summary>Returns current printer status; null if unavailable.</summary>
    Task<UsbPrinterStatus?> GetPrinterStatusAsync(string deviceId, CancellationToken ct = default);
}

/// <summary>A single file entry on a USB storage device.</summary>
public class UsbFileEntry
{
    public required string Path { get; set; }
    public required string FileName { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}

/// <summary>Snapshot of a printer's current state.</summary>
public class UsbPrinterStatus
{
    public bool IsReady { get; set; } = true;
    public bool HasPaper { get; set; } = true;
    public bool IsOnline { get; set; } = true;
    public string? StatusMessage { get; set; }
    public int JobsInQueue { get; set; }
}
