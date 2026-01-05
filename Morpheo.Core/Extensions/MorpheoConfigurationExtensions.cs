using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morpheo.Core.Configuration;
using Morpheo.Sdk;
using Morpheo.Core.Sync;

namespace Morpheo.Core.Extensions;

/// <summary>
/// Extension methods for loading and applying Morpheo configuration.
/// </summary>
public static class MorpheoConfigurationExtensions
{
    /// <summary>
    /// Loads the runtime configuration from 'morpheo.runtime.json' and applies settings.
    /// </summary>
    /// <param name="builder">The Morpheo builder.</param>
    /// <returns>The Morpheo builder.</returns>
    public static IMorpheoBuilder LoadRuntimeConfiguration(this IMorpheoBuilder builder)
    {
        // 1. Instantiate manager and load config
        var manager = new MorpheoConfigManager();
        var config = manager.Load();

        // 2. Register manager for later API access
        builder.Services.TryAddSingleton(manager);

        // 3. Apply basic configuration (Name and Port)
        builder.Configure(options =>
        {
            if (!string.IsNullOrWhiteSpace(config.NodeName))
            {
                options.NodeName = config.NodeName;
            }
            if (config.HttpPort > 0)
            {
                options.DiscoveryPort = config.HttpPort;
            }
        });

        // 4. Apply Network & Storage strategies

        // Mesh (P2P UDP)
        if (config.EnableMesh)
        {
            builder.AddMesh();
        }

        // Central Server (Uplink/Bridge HTTP)
        if (!string.IsNullOrWhiteSpace(config.CentralServerUrl))
        {
            // Verify URL validity before adding
            if (Uri.TryCreate(config.CentralServerUrl, UriKind.Absolute, out _))
            {
                builder.AddCentralServerRelay(config.CentralServerUrl);
            }
        }

        // Database
        switch (config.DatabaseType?.ToLowerInvariant())
        {
            case "sqlite":
            default:
                builder.UseSqlite(config.ConnectionString);
                break;
                
            // Extension points for future providers
            case "postgres":
                // builder.UsePostgres(config.ConnectionString);
                break;
            case "sqlserver":
                 // builder.UseSqlServer(config.ConnectionString);
                break;
        }

        return builder;
    }
}
