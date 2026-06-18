namespace Morpheo.Usb.Platform.Linux;

using System.Runtime.Versioning;

/// <summary>
/// Linux USB driver stub (libusb) — not yet implemented.
/// Use MockUsbDriver during development.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LibUsbDriver : IUsbDriver, IUsbDeviceEnumerator
{
    public Task<IReadOnlyList<UsbDeviceInfo>> EnumerateAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UsbDeviceInfo>>(Array.Empty<UsbDeviceInfo>());

    public Task<IReadOnlyList<UsbDeviceInfo>> EnumerateByClassAsync(UsbDeviceClass deviceClass, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UsbDeviceInfo>>(Array.Empty<UsbDeviceInfo>());

    public Task<UsbDeviceInfo?> GetDeviceAsync(string deviceId, CancellationToken ct = default)
        => Task.FromResult<UsbDeviceInfo?>(null);

    public Task<IReadOnlyList<UsbFileEntry>> ListFilesAsync(string deviceId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UsbFileEntry>>(Array.Empty<UsbFileEntry>());

    public Task<Stream?> ReadFileAsync(string deviceId, string filePath, CancellationToken ct = default)
        => Task.FromResult<Stream?>(null);

    public Task<long> WriteFileAsync(string deviceId, string filePath, Stream content, string contentType, CancellationToken ct = default)
        => Task.FromResult(0L);

    public Task DeleteFileAsync(string deviceId, string filePath, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SendPrintJobAsync(string deviceId, byte[] data, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<UsbPrinterStatus?> GetPrinterStatusAsync(string deviceId, CancellationToken ct = default)
        => Task.FromResult<UsbPrinterStatus?>(null);
}
