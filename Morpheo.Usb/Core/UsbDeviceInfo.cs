namespace Morpheo.Usb;

/// <summary>
/// Metadata describing a connected (or mock) USB device discovered by enumeration.
/// </summary>
public class UsbDeviceInfo
{
    /// <summary>Unique stable identifier (bus:device on Linux, instance path on Windows, or a mock ID).</summary>
    public required string Id { get; set; }

    /// <summary>Human-readable display label shown in the UI.</summary>
    public required string Label { get; set; }

    /// <summary>USB Vendor ID in hex (e.g. "0x0951" for Kingston).</summary>
    public required string VendorId { get; set; }

    /// <summary>USB Product ID in hex.</summary>
    public required string ProductId { get; set; }

    /// <summary>Manufacturer name from USB descriptor, if available.</summary>
    public string? Manufacturer { get; set; }

    /// <summary>Product name from USB descriptor, if available.</summary>
    public string? ProductName { get; set; }

    /// <summary>Device class (Storage, Printer, …).</summary>
    public required UsbDeviceClass DeviceClass { get; set; }

    /// <summary>Total capacity in bytes — only populated for Storage devices.</summary>
    public long? CapacityBytes { get; set; }

    /// <summary>Whether the device has been mounted in read-only mode.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>Hardware serial number, if available.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>Whether this is a simulated (mock) device rather than a real USB device.</summary>
    public bool IsMock { get; set; }

    /// <summary>When the device was first seen / inserted.</summary>
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
}
