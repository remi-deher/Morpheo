namespace Morpheo.Usb;

using Microsoft.Extensions.DependencyInjection;
using Morpheo.Usb.Audit;
using Morpheo.Usb.Platform.Mock;

/// <summary>
/// Extension method for registering the Morpheo USB subsystem in the DI container.
/// </summary>
public static class MorpheoUsbServiceExtensions
{
    /// <summary>
    /// Registers all USB sharing services. Always uses <see cref="MockUsbDriver"/> so
    /// containers without physical USB hardware work correctly.
    /// </summary>
    public static IServiceCollection AddMorpheoUsb(
        this IServiceCollection services,
        Action<UsbSharingOptions>? configure = null)
    {
        var options = new UsbSharingOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IUsbAccessPolicy, DefaultUsbAccessPolicy>();
        services.AddSingleton<IUsbAuditLogger, UsbAuditLogger>();

        // Always use mock driver (real platform drivers are future work)
        services.AddSingleton<MockUsbDriver>();
        services.AddSingleton<IUsbDeviceEnumerator>(sp => sp.GetRequiredService<MockUsbDriver>());
        services.AddSingleton<IUsbDriver>(sp => sp.GetRequiredService<MockUsbDriver>());

        return services;
    }
}
