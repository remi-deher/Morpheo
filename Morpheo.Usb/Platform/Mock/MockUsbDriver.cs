namespace Morpheo.Usb.Platform.Mock;

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

/// <summary>
/// Fully in-process USB simulator used in Docker / CI environments.
/// 
/// • Implements both IUsbDeviceEnumerator and IUsbDriver.
/// • Storage devices persist their files under {MockStorageRoot}/{deviceId}/.
/// • Printer devices keep a thread-safe in-memory print spooler.
/// • Devices can be added and ejected at runtime via AddDevice / EjectDevice.
/// </summary>
public sealed class MockUsbDriver : IUsbDeviceEnumerator, IUsbDriver
{
    private readonly string _storageRoot;
    private readonly ILogger<MockUsbDriver> _logger;

    // Live device registry
    private readonly ConcurrentDictionary<string, UsbDeviceInfo> _devices = new();

    // Print spooler: deviceId -> list of print records
    private readonly ConcurrentDictionary<string, List<MockPrintRecord>> _printJobs = new();
    private readonly Lock _printLock = new();

    public MockUsbDriver(UsbSharingOptions options, ILogger<MockUsbDriver> logger)
    {
        _storageRoot = options.MockStorageRoot;
        _logger = logger;

        // Seed two default mock devices so the UI has something to show on first boot.
        AddDevice(new UsbDeviceInfo
        {
            Id = "mock-storage-kingston-001",
            Label = "Kingston DataTraveler 16 GB",
            VendorId = "0x0951",
            ProductId = "0x1666",
            Manufacturer = "Kingston Technology",
            ProductName = "DataTraveler",
            DeviceClass = UsbDeviceClass.Storage,
            CapacityBytes = 16L * 1024 * 1024 * 1024,
            SerialNumber = "KST-001",
            IsMock = true,
        });

        AddDevice(new UsbDeviceInfo
        {
            Id = "mock-printer-zebra-001",
            Label = "Zebra ZD420 (USB)",
            VendorId = "0x0A5F",
            ProductId = "0x00D1",
            Manufacturer = "Zebra Technologies",
            ProductName = "ZD420",
            DeviceClass = UsbDeviceClass.Printer,
            SerialNumber = "ZBR-001",
            IsMock = true,
        });
    }

    // ── Device management ────────────────────────────────────────────────────

    /// <summary>Insert a new mock device (simulates plugging in a USB stick / printer).</summary>
    public void AddDevice(UsbDeviceInfo device)
    {
        device.DiscoveredAt = DateTime.UtcNow;
        _devices[device.Id] = device;

        if (device.DeviceClass == UsbDeviceClass.Storage)
        {
            var dir = StorageDir(device.Id);
            Directory.CreateDirectory(dir);
            _logger.LogInformation("[MockUSB] Storage device inserted: {Label} (dir: {Dir})", device.Label, dir);
        }
        else if (device.DeviceClass == UsbDeviceClass.Printer)
        {
            _printJobs.TryAdd(device.Id, new List<MockPrintRecord>());
            _logger.LogInformation("[MockUSB] Printer inserted: {Label}", device.Label);
        }
    }

    /// <summary>Eject (remove) a device from the mock bus.</summary>
    public bool EjectDevice(string deviceId)
    {
        if (_devices.TryRemove(deviceId, out var dev))
        {
            _logger.LogInformation("[MockUSB] Device ejected: {Label}", dev.Label);
            // NOTE: we deliberately keep the storage folder so data survives a re-insert.
            return true;
        }
        return false;
    }

    /// <summary>Returns the print spooler records for a printer device.</summary>
    public IReadOnlyList<MockPrintRecord> GetPrintJobs(string deviceId)
    {
        if (_printJobs.TryGetValue(deviceId, out var jobs))
            lock (_printLock) { return jobs.ToList().AsReadOnly(); }
        return Array.Empty<MockPrintRecord>();
    }

    /// <summary>Clears the print spooler for a printer device.</summary>
    public void ClearPrintJobs(string deviceId)
    {
        if (_printJobs.TryGetValue(deviceId, out var jobs))
            lock (_printLock) { jobs.Clear(); }
    }

    // ── IUsbDeviceEnumerator ─────────────────────────────────────────────────

    public Task<IReadOnlyList<UsbDeviceInfo>> EnumerateAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UsbDeviceInfo>>(_devices.Values.ToList().AsReadOnly());

    public Task<IReadOnlyList<UsbDeviceInfo>> EnumerateByClassAsync(
        UsbDeviceClass deviceClass, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UsbDeviceInfo>>(
            _devices.Values.Where(d => d.DeviceClass == deviceClass).ToList().AsReadOnly());

    public Task<UsbDeviceInfo?> GetDeviceAsync(string deviceId, CancellationToken ct = default)
        => Task.FromResult(_devices.GetValueOrDefault(deviceId));

    // ── IUsbDriver — Storage ─────────────────────────────────────────────────

    public Task<IReadOnlyList<UsbFileEntry>> ListFilesAsync(string deviceId, CancellationToken ct = default)
    {
        var dir = StorageDir(deviceId);
        if (!Directory.Exists(dir))
            return Task.FromResult<IReadOnlyList<UsbFileEntry>>(Array.Empty<UsbFileEntry>());

        var entries = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f =>
            {
                var info = new FileInfo(f);
                var relativePath = Path.GetRelativePath(dir, f).Replace('\\', '/');
                return new UsbFileEntry
                {
                    Path = "/" + relativePath,
                    FileName = info.Name,
                    ContentType = GuessContentType(info.Name),
                    SizeBytes = info.Length,
                    LastModified = info.LastWriteTimeUtc,
                };
            })
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<UsbFileEntry>>(entries);
    }

    public async Task<Stream?> ReadFileAsync(string deviceId, string filePath, CancellationToken ct = default)
    {
        var full = SafePath(deviceId, filePath);
        if (!File.Exists(full)) return null;

        var ms = new MemoryStream();
        await using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await fs.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    public async Task<long> WriteFileAsync(
        string deviceId, string filePath, Stream content, string contentType,
        CancellationToken ct = default)
    {
        var full = SafePath(deviceId, filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        await using var fs = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await content.CopyToAsync(fs, ct);
        return fs.Length;
    }

    public Task DeleteFileAsync(string deviceId, string filePath, CancellationToken ct = default)
    {
        var full = SafePath(deviceId, filePath);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    // ── IUsbDriver — Printing ────────────────────────────────────────────────

    public Task SendPrintJobAsync(string deviceId, byte[] data, CancellationToken ct = default)
    {
        if (!_printJobs.TryGetValue(deviceId, out var jobs))
        {
            _printJobs[deviceId] = jobs = new List<MockPrintRecord>();
        }

        var record = new MockPrintRecord
        {
            Id = Guid.NewGuid().ToString(),
            DeviceId = deviceId,
            RawBytes = data,
            TextPreview = TryDecodeUtf8(data),
            ReceivedAt = DateTime.UtcNow,
        };

        lock (_printLock) { jobs.Add(record); }

        _logger.LogInformation("[MockUSB] Print job received on {Device}: {Bytes} bytes", deviceId, data.Length);
        return Task.CompletedTask;
    }

    public Task<UsbPrinterStatus?> GetPrinterStatusAsync(string deviceId, CancellationToken ct = default)
    {
        if (!_devices.ContainsKey(deviceId)) return Task.FromResult<UsbPrinterStatus?>(null);

        _printJobs.TryGetValue(deviceId, out var jobs);
        return Task.FromResult<UsbPrinterStatus?>(new UsbPrinterStatus
        {
            IsReady = true,
            HasPaper = true,
            IsOnline = true,
            StatusMessage = "Ready (mock)",
            JobsInQueue = jobs?.Count ?? 0,
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string StorageDir(string deviceId)
        => Path.Combine(_storageRoot, SanitizeId(deviceId));

    private string SafePath(string deviceId, string filePath)
    {
        // Prevent path traversal
        var safe = filePath.TrimStart('/', '\\').Replace("..", "__");
        return Path.Combine(StorageDir(deviceId), safe);
    }

    private static string SanitizeId(string id)
        => string.Concat(id.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));

    private static string GuessContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".zip" => "application/zip",
        _ => "application/octet-stream",
    };

    private static string TryDecodeUtf8(byte[] data)
    {
        try { return System.Text.Encoding.UTF8.GetString(data); }
        catch { return $"<binary {data.Length} bytes>"; }
    }
}

/// <summary>A single record in the mock printer spooler.</summary>
public class MockPrintRecord
{
    public required string Id { get; set; }
    public required string DeviceId { get; set; }
    public byte[] RawBytes { get; set; } = Array.Empty<byte>();
    public string? TextPreview { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
