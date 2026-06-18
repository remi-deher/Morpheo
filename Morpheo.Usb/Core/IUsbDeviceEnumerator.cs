namespace Morpheo.Usb;

/// <summary>
/// Platform-independent interface for discovering connected USB devices.
/// Implementations: MockUsbDriver (Docker/tests), WinUsbDriver (Windows), LibUsbDriver (Linux).
/// </summary>
public interface IUsbDeviceEnumerator
{
    /// <summary>Returns all currently connected USB devices.</summary>
    Task<IReadOnlyList<UsbDeviceInfo>> EnumerateAsync(CancellationToken ct = default);

    /// <summary>Returns devices matching a specific class (Storage, Printer, …).</summary>
    Task<IReadOnlyList<UsbDeviceInfo>> EnumerateByClassAsync(UsbDeviceClass deviceClass, CancellationToken ct = default);

    /// <summary>Gets detailed info about a single device by its Id; null if not found or ejected.</summary>
    Task<UsbDeviceInfo?> GetDeviceAsync(string deviceId, CancellationToken ct = default);
}
