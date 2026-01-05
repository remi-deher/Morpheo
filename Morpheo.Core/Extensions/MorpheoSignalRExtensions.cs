using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using Morpheo.Core.Configuration;
using Morpheo.Core.SignalR;
using Morpheo.Core.Sync.Strategies;
using Morpheo.Sdk;

namespace Morpheo.Core.Extensions;

/// <summary>
/// Extension methods for SignalR integration.
/// </summary>
public static class MorpheoSignalRExtensions
{
    /// <summary>
    /// Enables SERVER mode (Listen for connections).
    /// </summary>
    /// <param name="builder">The Morpheo builder.</param>
    /// <returns>The Morpheo builder.</returns>
    public static IMorpheoBuilder AddSignalRServer(this IMorpheoBuilder builder)
    {
        // 1. Activate SignalR in ASP.NET Core
        builder.Services.AddSignalR();

        // 2. Register Internal Broadcast Strategy (Server -> Clients)
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISyncStrategyProvider>(sp =>
        {
            var hubContext = sp.GetRequiredService<IHubContext<MorpheoSyncHub>>();
            return new SignalRServerBroadcastStrategy(hubContext);
        }));

        return builder;
    }

    /// <summary>
    /// Enables CLIENT mode (Connect to a server).
    /// </summary>
    /// <param name="builder">The Morpheo builder.</param>
    /// <param name="serverHubUrl">The URL of the remote SignalR Hub.</param>
    /// <returns>The Morpheo builder.</returns>
    public static IMorpheoBuilder AddSignalRClient(this IMorpheoBuilder builder, string serverHubUrl)
    {
        if (string.IsNullOrWhiteSpace(serverHubUrl)) throw new ArgumentNullException(nameof(serverHubUrl));

        // 1. Register Client Strategy (Local -> Server)
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISyncStrategyProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SignalRClientStrategy>>();
            // IServiceProvider is passed to resolve DataSyncService within the handler
            return new SignalRClientStrategy(serverHubUrl, sp, logger);
        }));

        return builder;
    }
}
