using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morpheo.Core.Configuration;
using Morpheo.Core.Data;
using Morpheo.Core.Sync.Strategies;
using Morpheo.Sdk;

namespace Morpheo.Core.Extensions;

/// <summary>
/// Provides extension methods for configuring storage providers within the Morpheo framework.
/// </summary>
public static class MorpheoStorageExtensions
{
    /// <summary>
    /// Configures the Morpheo node to use SQLite as the internal storage provider for synchronization logs and metadata.
    /// </summary>
    /// <param name="builder">The <see cref="IMorpheoBuilder"/> to configure.</param>
    /// <param name="connectionString">
    /// The connection string to the SQLite database. 
    /// If null or empty, a default database named 'morpheo.db' will be created in the local application data folder.
    /// </param>
    /// <returns>The <see cref="IMorpheoBuilder"/> instance for chaining configurations.</returns>
    public static IMorpheoBuilder UseSqlite(this IMorpheoBuilder builder, string? connectionString = null)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var dbPath = System.IO.Path.Join(path, "morpheo.db");
            connectionString = $"Data Source={dbPath}";
        }

        builder.Services.AddDbContext<MorpheoDbContext>(options =>
            options.UseSqlite(connectionString));

        return builder;
    }

    /// <summary>
    /// Registers an external database context to be synchronized by the Morpheo framework.
    /// </summary>
    /// <typeparam name="TContext">The type of the external <see cref="DbContext"/> to synchronize.</typeparam>
    /// <param name="builder">The <see cref="IMorpheoBuilder"/> to configure.</param>
    /// <param name="optionsAction">A delegate to configure the <see cref="DbContextOptions"/> for the external context.</param>
    /// <returns>The <see cref="IMorpheoBuilder"/> instance for chaining configurations.</returns>
    /// <remarks>
    /// This method registers a <see cref="ISyncStrategyProvider"/> that enables Morpheo to monitor and propagate changes 
    /// occurring in the specified <typeparamref name="TContext"/>.
    /// </remarks>
    public static IMorpheoBuilder AddExternalDbSync<TContext>(
        this IMorpheoBuilder builder,
        Action<DbContextOptionsBuilder> optionsAction)
        where TContext : DbContext
    {
        builder.Services.AddDbContext<TContext>(optionsAction);

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ISyncStrategyProvider, ExternalDbSyncStrategy<TContext>>());

        return builder;
    }
}
