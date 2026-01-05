using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Runtime.Versioning;
using Morpheo.Sdk;
using Morpheo.Core.Client;
using Morpheo.Core.Configuration;
using Morpheo.Core.Data;
using Morpheo.Core.Discovery;
using Morpheo.Core.Printers;
using Morpheo.Core.Server;
using Morpheo.Core.Sync;
using Morpheo.Core.Sync.Strategies;
using Morpheo.Core.Security;

namespace Morpheo.Core;

/// <summary>
/// Extension methods for registering Morpheo services in the dependency injection container.
/// </summary>
public static class MorpheoServiceExtensions
{
    /// <summary>
    /// Adds Morpheo core services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional delegate to configure the builder.</param>
    /// <returns>The Morpheo builder.</returns>
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
        var typeResolver = new SimpleTypeResolver();
        services.AddSingleton<IEntityTypeResolver>(typeResolver);
        services.AddSingleton(typeResolver);

        services.AddSingleton<ConflictResolutionEngine>();

        // Default strategy: Composite
        services.AddSingleton<ISyncRoutingStrategy, CompositeRoutingStrategy>();

        // Add a "Null" provider so the list is never empty
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISyncStrategyProvider, NullStrategyProvider>());

        services.AddSingleton<DataSyncService>();

        services.AddHostedService<LogCompactionService>();

        services.TryAddSingleton<IPrintGateway, NullPrintGateway>();
        services.TryAddSingleton<IRequestAuthenticator, AllowAllAuthenticator>();

        return builder;
    }

    /// <summary>
    /// Configures Morpheo options (e.g., NodeName, Port) via the Builder.
    /// </summary>
    /// <param name="builder">The Morpheo builder.</param>
    /// <param name="configure">The configuration delegate.</param>
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
    /// Adds Windows-specific printing services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddWindowsPrinting(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IPrintGateway, WindowsPrinterService>());
        return services;
    }
}
