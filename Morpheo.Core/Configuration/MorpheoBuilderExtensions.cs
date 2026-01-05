using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Morpheo.Core.Configuration;
using Morpheo.Core.Extensions;
using Morpheo.Core.Sync;
using Morpheo.Core.Sync.Strategies;
using Morpheo.Core.Security;
using Morpheo.Core.Data;
using Morpheo.Core.Printers;
using Morpheo.Sdk;

namespace Morpheo.Core.Configuration;

/// <summary>
/// Fluent API extensions for configuring Morpheo strategies and components.
/// </summary>
public static class MorpheoBuilderExtensions
{
    // --- CLOCKS ---

    /// <summary>
    /// Configures Morpheo to use Vector Clocks (High Accuracy).
    /// </summary>
    public static IMorpheoBuilder UseVectorClocks(this IMorpheoBuilder builder)
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<ILogicalClock, VectorClockService>());
        return builder;
    }

    /// <summary>
    /// Configures Morpheo to use Hybrid Logical Clocks (High Scalability).
    /// </summary>
    public static IMorpheoBuilder UseHybridLogicalClocks(this IMorpheoBuilder builder)
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<ILogicalClock, HybridClockService>());
        return builder;
    }

    // --- TRANSPORT ---

    /// <summary>
    /// Enables Binary Transport (MessagePack) optimizations.
    /// </summary>
    public static IMorpheoBuilder UseBinaryTransport(this IMorpheoBuilder builder)
    {
        return builder;
    }

    // --- STRATEGIES (Network Routing) ---

    /// <summary>
    /// Enables the Gossip (Epidemic) routing strategy.
    /// Best for large clusters (swarms).
    /// </summary>
    public static IMorpheoBuilder UseGossipStrategy(this IMorpheoBuilder builder, int fanout = 3)
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<ISyncRoutingStrategy, GossipRoutingStrategy>());
        builder.Services.TryAddSingleton<MerkleTreeService>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AntiEntropyService>());
        return builder;
    }

    /// <summary>
    /// Enables the Flood (Broadcast) strategy.
    /// Best for small, low-latency LAN clusters (< 50 nodes).
    /// </summary>
    public static IMorpheoBuilder UseFloodStrategy(this IMorpheoBuilder builder)
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<ISyncRoutingStrategy, MeshBroadcastStrategy>());
        var antiEntropyDescriptor = builder.Services.FirstOrDefault(d => d.ImplementationType == typeof(AntiEntropyService));
        if (antiEntropyDescriptor != null)
        {
            builder.Services.Remove(antiEntropyDescriptor);
        }
        return builder;
    }

    // --- STORAGE CONFIGURATION (Database) ---

    /// <summary>
    /// Configures the node to use SQLite as the database provider.
    /// Also registers IDbContextFactory for background services.
    /// </summary>
    /// <param name="builder">The Morpheo builder.</param>
    /// <param name="connectionString">Optional connection string. If null, a default local path is used.</param>
    /// <returns>The Morpheo builder.</returns>
    public static IMorpheoBuilder UseSqlite(this IMorpheoBuilder builder, string? connectionString = null)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var dbPath = System.IO.Path.Join(path, "morpheo.db");
            connectionString = $"Data Source={dbPath}";
        }

        // Register standard DbContext (Scoped)
        builder.Services.AddDbContext<MorpheoDbContext>(options =>
            options.UseSqlite(connectionString));

        // Register Factory (Singleton/Scoped) for Background Services
        builder.Services.AddDbContextFactory<MorpheoDbContext>(options =>
            options.UseSqlite(connectionString));

        return builder;
    }

    // --- STORAGE STRATEGIES (Logs) ---

    /// <summary>
    /// Uses FileLogStore (LSM) for high-performance log storage.
    /// </summary>
    public static IMorpheoBuilder UseFileLogStore(this IMorpheoBuilder builder, string? storagePath = null)
    {
        if (storagePath != null)
        {
            builder.Services.Configure<MorpheoOptions>(opt => 
            {
                if (Path.IsPathRooted(storagePath))
                    opt.LocalStoragePath = storagePath;
                else
                    opt.LocalStoragePath = Path.Combine(opt.LocalStoragePath ?? AppContext.BaseDirectory, storagePath);
            });
        }

        builder.Services.AddSingleton<FileLogStore>();
        builder.Services.Replace(ServiceDescriptor.Singleton<ISyncLogStore>(sp => sp.GetRequiredService<FileLogStore>()));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<FileLogStore>());

        return builder;
    }

    /// <summary>
    /// Uses SqlSyncLogStore for log storage (Archival only).
    /// </summary>
    public static IMorpheoBuilder UseSqlLogStore(this IMorpheoBuilder builder)
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<ISyncLogStore, SqlSyncLogStore>());
        return builder;
    }

    /// <summary>
    /// Uses HybridLogStore (Hot File Store + Cold SQL Store).
    /// Provides infinite retention with high write throughput.
    /// Automatically manages FileLogStore and Archiving background services.
    /// </summary>
    public static IMorpheoBuilder UseHybridLogStore(this IMorpheoBuilder builder)
    {
        // 1. Register components (Self-contained)
        builder.Services.AddSingleton<FileLogStore>();
        builder.Services.AddSingleton<SqlSyncLogStore>();

        // 2. Register Hybrid as Main Store
        builder.Services.Replace(ServiceDescriptor.Singleton<ISyncLogStore, HybridLogStore>());

        // 3. Helper to get Hybrid instance efficiently if needed directly
        builder.Services.AddSingleton(sp => (HybridLogStore)sp.GetRequiredService<ISyncLogStore>());

        // 4. Register Hosted Services for Background Tasks (Flush + Archive)
        builder.Services.AddHostedService(sp => sp.GetRequiredService<FileLogStore>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<HybridLogStore>());

        return builder;
    }

    /// <summary>
    /// Registers an external database synchronization strategy.
    /// </summary>
    public static IMorpheoBuilder AddExternalDbSync<TContext>(
        this IMorpheoBuilder builder,
        Action<DbContextOptionsBuilder> optionsAction)
        where TContext : DbContext
    {
        // 1. Register External DbContext
        builder.Services.AddDbContext<TContext>(optionsAction);

        // 2. Register Strategy
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ISyncStrategyProvider, ExternalDbSyncStrategy<TContext>>());

        return builder;
    }

    // --- PRINTING ---

    /// <summary>
    /// Enables native RAW printing (ZPL/ESC-POS) capabilities.
    /// On Windows, registers the WinSpool P/Invoke service.
    /// </summary>
    public static IMorpheoBuilder UseNativePrinting(this IMorpheoBuilder builder)
    {
        if (OperatingSystem.IsWindows())
        {
            builder.Services.AddSingleton<IPrintGateway, WindowsPrinterService>();
        }
        return builder;
    }

    // --- ENROLLMENT (SECURITY) ---

    public static IMorpheoBuilder UseAutoEnrollment(this IMorpheoBuilder builder)
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<INodeEnrollmentStrategy, AutoAcceptEnrollmentStrategy>());
        return builder;
    }

    public static IMorpheoBuilder UseManualEnrollment(this IMorpheoBuilder builder)
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<INodeEnrollmentStrategy, ManualApprovalEnrollmentStrategy>());
        return builder;
    }

    public static IMorpheoBuilder UseSharedSecret(this IMorpheoBuilder builder)
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<INodeEnrollmentStrategy>(sp => 
            new SharedSecretEnrollmentStrategy(
                sp.GetRequiredService<IPeerTrustStore>(),
                sp.GetRequiredService<MorpheoConfigManager>()
            )));
        return builder;
    }

    public static IMorpheoBuilder UseFileTrustStore(this IMorpheoBuilder builder, string filePath)
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<IPeerTrustStore>(_ => new FilePeerTrustStore(filePath)));
        return builder;
    }

    // --- UI ---

    public static IMorpheoBuilder UseCustomDashboard(this IMorpheoBuilder builder, string folderPath)
    {
        var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(DashboardOptions));
        if (descriptor != null)
        {
             builder.Services.Remove(descriptor);
        }
        builder.Services.AddSingleton(new DashboardOptions { CustomUiPath = folderPath });
        return builder;
    }
}
