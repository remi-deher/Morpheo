namespace Morpheo.Usb;

/// <summary>Configuration options for the USB sharing subsystem.</summary>
public class UsbSharingOptions
{
    /// <summary>Enable USB mass-storage device access. Default: true.</summary>
    public bool EnableStorage { get; set; } = true;

    /// <summary>Enable USB printer access. Default: true.</summary>
    public bool EnablePrinting { get; set; } = true;

    /// <summary>
    /// When true, the MockUsbDriver is always used (ideal for Docker containers and CI).
    /// When false, the platform-native driver is selected.
    /// </summary>
    public bool UseMockDriver { get; set; } = true;

    /// <summary>
    /// Root directory where the MockUsbDriver persists mock device filesystems.
    /// Each device gets its own subdirectory: {MockStorageRoot}/{deviceId}/
    /// </summary>
    public string MockStorageRoot { get; set; } = "/app/data/mock-usb";

    /// <summary>
    /// If set, only USB devices whose VendorId is in this set will be enumerated.
    /// null means all vendors are allowed.
    /// </summary>
    public IReadOnlySet<string>? AllowedVendorIds { get; set; }

    /// <summary>
    /// VendorIds that are explicitly blocked from enumeration.
    /// </summary>
    public IReadOnlySet<string>? BlockedVendorIds { get; set; }

    /// <summary>
    /// Per-principal device allowlist. Maps principalId → set of allowed deviceIds.
    /// null means no allowlist (open access).
    /// </summary>
    public Dictionary<string, HashSet<string>>? PrincipalDeviceAllowlist { get; set; }

    /// <summary>Maximum number of concurrent USB file transfers per principal.</summary>
    public int MaxConcurrentTransfersPerPrincipal { get; set; } = 10;
}
