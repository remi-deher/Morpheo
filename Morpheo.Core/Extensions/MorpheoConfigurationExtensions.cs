using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morpheo.Core.Configuration;
using Morpheo.Sdk;
using Morpheo.Core.Sync;

namespace Morpheo;

/// <summary>
/// Extension methods for loading and applying Morpheo configuration.
/// </summary>
public static class MorpheoConfigurationExtensions
{
    /// <summary>
    /// Loads the Morpheo configuration from the 'morpheo.runtime.json' file and applies it.
    /// Use this for "Configuration-First" setups instead of fluent code configuration.
    /// </summary>
    /// <remarks>
    /// This method automatically handles:
    /// <list type="bullet">
    /// <item><description>Node Name and Port</description></item>
    /// <item><description>Enabling Mesh (P2P)</description></item>
    /// <item><description>Connecting to a Central Server (Uplink)</description></item>
    /// <item><description>Database Provider Selection (currently SQLite)</description></item>
    /// </list>
    /// </remarks>
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
