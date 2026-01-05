using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morpheo.Core.Configuration;
using Morpheo.Core.Discovery;
using Morpheo.Core.Server;
using Morpheo.Core.Sync.Strategies;
using Morpheo.Sdk;

namespace Morpheo;

/// <summary>
/// Extension methods for configuring network features.
/// </summary>
public static class MorpheoNetworkExtensions
{
    /// <summary>
    /// Enables local network discovery via UDP Broadcast.
    /// This allows nodes to find each other without a central server.
    /// </summary>
    /// <remarks>
    /// Registers <see cref="UdpDiscoveryService"/> as the implementation of <see cref="INetworkDiscovery"/>.
    /// </remarks>
    /// <param name="builder">The Morpheo builder.</param>
    /// <returns>The Morpheo builder.</returns>
    public static IMorpheoBuilder UseUdpDiscovery(this IMorpheoBuilder builder)
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<INetworkDiscovery, UdpDiscoveryService>());
        return builder;
    }

    /// <summary>
    /// Enables full Mesh Networking capabilities (Zero-Conf).
    /// </summary>
    /// <remarks>
    /// This method activates:
    /// <list type="bullet">
    /// <item><description>UDP Discovery (to find peers)</description></item>
    /// <item><description>MorpheoWebServer (Kestrel, to receive sync requests)</description></item>
    /// <item><description>MeshBroadcastStrategy (to propagate updates to peers)</description></item>
    /// </list>
    /// </remarks>
    /// <param name="builder">The Morpheo builder.</param>
    /// <returns>The Morpheo builder.</returns>
    public static IMorpheoBuilder AddMesh(this IMorpheoBuilder builder)
    {
        // 1. UDP Discovery
        builder.UseUdpDiscovery();

        // 2. Web Server (Kestrel)
        builder.Services.Replace(ServiceDescriptor.Singleton<IMorpheoServer, MorpheoWebServer>());

        // 3. Mesh Routing Strategy (Added as Provider, not Replace)
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISyncStrategyProvider, MeshBroadcastStrategy>());

        return builder;
    }
}
