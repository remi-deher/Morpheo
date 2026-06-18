namespace Morpheo.Usb;

/// <summary>Which kind of operation a principal is requesting on a USB device.</summary>
public enum UsbAccessType
{
    Read = 1,
    Write = 2,
    Execute = 4,
}

/// <summary>
/// Authorization policy: governs who may access which USB devices and how.
/// </summary>
public interface IUsbAccessPolicy
{
    /// <summary>
    /// Returns true when <paramref name="principalId"/> is allowed to perform
    /// <paramref name="accessType"/> on <paramref name="device"/>.
    /// </summary>
    Task<bool> CanAccessDeviceAsync(
        string principalId,
        UsbDeviceInfo device,
        UsbAccessType accessType,
        CancellationToken ct = default);
}
