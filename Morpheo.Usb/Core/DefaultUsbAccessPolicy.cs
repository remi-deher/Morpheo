namespace Morpheo.Usb;

using Microsoft.Extensions.Logging;

/// <summary>
/// Default access policy: open access (all principals, all devices).
/// In production, override with a JWT/RBAC-based implementation.
/// </summary>
public class DefaultUsbAccessPolicy : IUsbAccessPolicy
{
    private readonly UsbSharingOptions _options;
    private readonly ILogger<DefaultUsbAccessPolicy> _logger;

    public DefaultUsbAccessPolicy(UsbSharingOptions options, ILogger<DefaultUsbAccessPolicy> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<bool> CanAccessDeviceAsync(
        string principalId,
        UsbDeviceInfo device,
        UsbAccessType accessType,
        CancellationToken ct = default)
    {
        // "admin" or an empty/anonymous principal always gets access in the demo.
        if (string.IsNullOrEmpty(principalId) || principalId == "anonymous" || principalId == "admin")
            return Task.FromResult(true);

        // Check explicit allowlist when one is configured.
        if (_options.PrincipalDeviceAllowlist is { } allowlist)
        {
            if (allowlist.TryGetValue(principalId, out var allowed))
                return Task.FromResult(allowed.Contains(device.Id));

            _logger.LogWarning("USB access denied — {Principal} not in allowlist for {Device}", principalId, device.Id);
            return Task.FromResult(false);
        }

        // No allowlist configured → open access.
        return Task.FromResult(true);
    }
}
