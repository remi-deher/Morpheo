using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Morpheo.Core;

/// <summary>
/// Extension methods for configuring the Morpheo worker.
/// </summary>
public static class HostingExtensions
{
    /// <summary>
    /// Configures the host to run as a Morpheo worker, adapting to the underlying operating system (Windows Service, Systemd, etc.).
    /// </summary>
    /// <param name="builder">The host builder.</param>
    /// <returns>The configured host builder.</returns>
    public static IHostBuilder UseMorpheoWorker(this IHostBuilder builder)
    {
        if (OperatingSystem.IsWindows())
        {
            builder.UseWindowsService();
        }
        else if (OperatingSystem.IsLinux())
        {
            builder.UseSystemd();
        }
        else if (OperatingSystem.IsAndroid())
        {
            // Android: The service is managed by the Activity or a native Android Service.
            // Only registering MorpheoNode for injection.
            builder.ConfigureServices(services =>
            {
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MorpheoNode>());
            });
        }
        // For other OS (macOS, iOS, etc.), we use the standard GenericHost lifecycle by default.
        
        return builder;
    }
}
