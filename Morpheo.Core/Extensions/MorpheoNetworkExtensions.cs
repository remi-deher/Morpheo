using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morpheo.Core.Configuration;
using Morpheo.Core.Discovery;
using Morpheo.Core.Server;
using Morpheo.Core.Sync.Strategies;
using Morpheo.Sdk;

namespace Morpheo.Core.Extensions;

/// <summary>
/// Extension methods for configuring network features.
/// </summary>
public static class MorpheoNetworkExtensions
{
    /// <summary>
    /// Enables full Mesh Networking capabilities (UDP Discovery + P2P Sync).
    /// </summary>
    /// <param name="builder">The Morpheo builder.</param>
    /// <returns>The Morpheo builder.</returns>
    public static IMorpheoBuilder AddMesh(this IMorpheoBuilder builder)
    {
        // 1. UDP Discovery
        builder.Services.Replace(ServiceDescriptor.Singleton<INetworkDiscovery, UdpDiscoveryService>());

        // 2. Web Server (Kestrel)
        builder.Services.Replace(ServiceDescriptor.Singleton<IMorpheoServer, MorpheoWebServer>());

        // 3. Mesh Routing Strategy (Added as Provider, not Replace)
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISyncStrategyProvider, MeshBroadcastStrategy>());

        return builder;
    }
}
