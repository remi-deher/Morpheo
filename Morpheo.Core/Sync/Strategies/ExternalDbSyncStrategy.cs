using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;
using Morpheo.Core.Data;

namespace Morpheo.Core.Sync.Strategies;

/// <summary>
/// Synchronization strategy that pushes changes to an external database (e.g., PostgreSQL, SQL Server).
/// </summary>
/// <typeparam name="TContext">The type of the external DbContext.</typeparam>
public class ExternalDbSyncStrategy<TContext> : ISyncStrategyProvider
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SimpleTypeResolver _typeResolver;
    private readonly ILogger<ExternalDbSyncStrategy<TContext>> _logger;

    public ExternalDbSyncStrategy(
        IServiceScopeFactory scopeFactory,
        SimpleTypeResolver typeResolver,
        ILogger<ExternalDbSyncStrategy<TContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _typeResolver = typeResolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task PropagateAsync(
        SyncLogDto log,
        IEnumerable<PeerInfo> candidates,
        Func<PeerInfo, SyncLogDto, Task<bool>> sendFunc)
    {
        try
        {
            var entityType = _typeResolver.ResolveType(log.EntityName);
            if (entityType == null) return;

            // Deserialization
            var entity = JsonSerializer.Deserialize(log.JsonData, entityType);
            if (entity == null) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();

            // Assuming the entity has an "Id" property matching EntityId
            // Or searching by primary key if supported.
            var existing = await db.FindAsync(entityType, log.EntityId);

            if (existing != null)
            {
                // Update
                db.Entry(existing).CurrentValues.SetValues(entity);
            }
            else
            {
                // Insert
                db.Add(entity);
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to propagate sync to external database for entity {EntityName}", log.EntityName);
            // Do not rethrow to avoid blocking main sync flow
        }
    }
}
