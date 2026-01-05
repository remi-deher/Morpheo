using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Runtime.Versioning;
using System.IO;
using Morpheo.Sdk;
using Morpheo.Sdk.Blobs;
using Morpheo.Core.Blobs;
using Morpheo.Core.Client;
using Morpheo.Core.Configuration;
using Morpheo.Core.Data;
using Morpheo.Core.Discovery;
using Morpheo.Core;
using Morpheo.Core.Printers;
using Morpheo.Core.Server;
using Morpheo.Core.Sync;
using Morpheo.Core.Sync.Strategies;
using Morpheo.Core.Security;

namespace Morpheo;

/// <summary>
/// Extension methods for registering Morpheo services in the dependency injection container.
/// </summary>
public static class MorpheoServiceExtensions
{
    /// <summary>
    /// Adds Morpheo core services to the service collection.
    /// This is the entry point for configuring the Morpheo framework.
    /// </summary>
    /// <remarks>
    /// This method registers:
    /// <list type="bullet">
    /// <item><description>Core Infrastructure (Node, HttpClient, DbContext)</description></item>
    /// <item><description>Synchronization Engine (Conflict Resolution, Vector Clocks)</description></item>
    /// <item><description>Default Strategies (Gossip Routing, File Log Storage)</description></item>
    /// <item><description>Background Services (AntiEntropy, BlobSync, LogCompaction)</description></item>
    /// <item><description>Security Defaults (Auto-Enrollment)</description></item>
    /// </list>
    /// </remarks>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">An optional delegate to configure the <see cref="IMorpheoBuilder"/>.</param>
    /// <returns>A <see cref="IMorpheoBuilder"/> that can be used to further configure Morpheo.</returns>
    public static IMorpheoBuilder AddMorpheo(
        this IServiceCollection services,
        Action<IMorpheoBuilder>? configure = null)
    {
        // 1. Configuration
        var options = new MorpheoOptions();
        options.Validate();
        services.AddSingleton(options);

        var builder = new MorpheoBuilder(services);

        // 2. User Configuration
        configure?.Invoke(builder);

        // 3. Core Infrastructure Services
        services.AddSingleton<MorpheoNode>();
        
        // Registering Null/NoOp versions by default
        services.TryAddSingleton<INetworkDiscovery, NullNetworkDiscovery>();
        services.TryAddSingleton<IMorpheoServer, NullMorpheoServer>();
        
        services.AddHttpClient();
        services.AddSingleton<IMorpheoClient, MorpheoHttpClient>();
        
        services.AddSingleton<DatabaseInitializer>();

        // 4. Synchronization Engine
        var typeResolver = new AttributeTypeResolver();
        services.AddSingleton<IEntityTypeResolver>(typeResolver);

        services.AddSingleton<ConflictResolutionEngine>();

        // Default Clock: Vector Clocks (can be overridden by UseHybridLogicalClocks)
        services.TryAddSingleton<ILogicalClock, VectorClockService>();

        // Default strategy: Gossip (switch from Composite/Flood)
        services.AddSingleton<ISyncRoutingStrategy, GossipRoutingStrategy>();

        // Add a "Null" provider so the list is never empty
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISyncStrategyProvider, NullStrategyProvider>());

        services.AddSingleton<DataSyncService>();

        // Compression
        services.AddSingleton<DeltaCompressionService>();

        // Log Storage (LSM)
        services.AddSingleton<FileLogStore>();
        services.AddSingleton<ISyncLogStore>(sp => sp.GetRequiredService<FileLogStore>());
        services.AddHostedService(sp => sp.GetRequiredService<FileLogStore>());
        
        // DbContext Factory for thread-safe, short-lived contexts
        services.AddDbContextFactory<MorpheoDbContext>();

        services.AddHostedService<LogCompactionService>();

        services.AddSingleton<MerkleTreeService>();
        services.AddHostedService<AntiEntropyService>();

        // Ensure BlobStore exists to prevent BlobSyncService crash
        if (!services.Any(d => d.ServiceType == typeof(IMorpheoBlobStore)))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var tempPath = Path.Combine(appData, "Morpheo", "Blobs");
            services.Configure<FileSystemBlobStoreOptions>(opts => opts.RootPath = tempPath);
            services.AddSingleton<IMorpheoBlobStore, FileSystemBlobStore>();
        }
        services.AddHostedService<BlobSyncService>();

        // Security Defaults
        services.TryAddSingleton<IPeerTrustStore, InMemoryPeerTrustStore>();
        services.TryAddSingleton<INodeEnrollmentStrategy, AutoAcceptEnrollmentStrategy>();

        services.TryAddSingleton<IPrintGateway, NullPrintGateway>();
        services.TryAddSingleton<IRequestAuthenticator, AllowAllAuthenticator>();

        return builder;
    }

    /// <summary>
    /// Configures global Morpheo options.
    /// Use this to set the Node Name, Role, or Discovery Port.
    /// </summary>
    /// <param name="builder">The Morpheo builder.</param>
    /// <param name="configure">A delegate to configure the <see cref="MorpheoOptions"/>.</param>
    /// <returns>The Morpheo builder.</returns>
    public static IMorpheoBuilder Configure(this IMorpheoBuilder builder, Action<MorpheoOptions> configure)
    {
        var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(MorpheoOptions));
        if (descriptor?.ImplementationInstance is MorpheoOptions options)
        {
            configure(options);
        }
        return builder;
    }

    /// <summary>
    /// Adds Windows-specific printing services (WinSpool).
    /// This enables the node to act as a Print Gateway for raw ZPL/ESC-POS commands.
    /// </summary>
    /// <remarks>
    /// Requires Windows OS. Replaces the default <see cref="IPrintGateway"/> with <see cref="WindowsPrinterService"/>.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddWindowsPrinting(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IPrintGateway, WindowsPrinterService>());
        return services;
    }
}
