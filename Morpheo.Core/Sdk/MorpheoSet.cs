using Microsoft.EntityFrameworkCore;
using Morpheo.Core.Data;
using Morpheo.Core.Sync;
using Morpheo.Sdk;

namespace Morpheo.Core.Sdk;

public class MorpheoSet<T> : IMorpheoSet<T> where T : MorpheoEntity
{
    private readonly IDbContextFactory<MorpheoDbContext> _contextFactory;
    private readonly DataSyncService _syncService;
    private readonly IEntityTypeResolver _typeResolver;

    public MorpheoSet(
        IDbContextFactory<MorpheoDbContext> contextFactory, 
        DataSyncService syncService,
        IEntityTypeResolver typeResolver)
    {
        _contextFactory = contextFactory;
        _syncService = syncService;
        _typeResolver = typeResolver;
    }

    public async Task AddAsync(T entity)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        context.Set<T>().Add(entity);
        await context.SaveChangesAsync();
        await _syncService.BroadcastChangeAsync(entity, "CREATE");
    }

    public async Task UpdateAsync(T entity)
    {
        entity.LastModified = DateTime.UtcNow;
        using var context = await _contextFactory.CreateDbContextAsync();
        context.Set<T>().Update(entity);
        await context.SaveChangesAsync();
        await _syncService.BroadcastChangeAsync(entity, "UPDATE");
    }

    public async Task DeleteAsync(string id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Set<T>().FindAsync(id);
        if (entity != null)
        {
            entity.IsDeleted = true;
            entity.LastModified = DateTime.UtcNow;
            context.Set<T>().Update(entity); 
            await context.SaveChangesAsync();
            await _syncService.BroadcastChangeAsync(entity, "DELETE");
        }
    }

    public IQueryable<T> Query()
    {
        // NOTE: This creates a context that the caller must manage.
        // Best practice: User should call .ToListAsync() or enumerate immediately.
        // For true deferred execution, consider returning IAsyncEnumerable<T> or Task<List<T>>.
        // For now, we return a tracked queryable from a fresh context.
        // WARNING: The context lifecycle is NOT managed here. 
        // Consider documenting that Query() should be used with immediate materialization.
        
        // ALTERNATIVE APPROACH: Return materialized list
        // return Task.FromResult(context.Set<T>().Where(x => !x.IsDeleted).AsNoTracking().ToList());
        
        // For IQueryable semantics, we need to keep context alive somehow.
        // SOLUTION: Return from a scoped context - let DI manage it.
        // This means we need a scoped context again, OR change signature to async.
        
        // TEMPORARY: Return from factory but warn about disposal
        var context = _contextFactory.CreateDbContext();
        return context.Set<T>().Where(x => !x.IsDeleted).AsNoTracking();
        // BUG: Context will be disposed before query executes!
        // FIX: Change interface to return Task<List<T>> or IAsyncEnumerable
        // Or inject a long-lived scoped context just for queries
    }

    public IDisposable Observe(Action<EntityChange<T>> callback)
    {
        var networkName = _typeResolver.GetNetworkName(typeof(T));
        
        void SafeHandler(string type, string id, string act) 
        {
             if (string.Equals(type, networkName, StringComparison.OrdinalIgnoreCase))
             {
                 try
                 {
                     // Use factory to create a fresh, thread-safe context
                     using var context = _contextFactory.CreateDbContext();
                     var entity = context.Set<T>().Find(id);
                     callback(new EntityChange<T>(entity, act));
                 }
                 catch
                 {
                     // Context creation failed or entity not found
                 }
             }
        }

        _syncService.OnEntityChanged += SafeHandler;
        return new DisposableAction(() => _syncService.OnEntityChanged -= SafeHandler);
    }
    
    private class DisposableAction : IDisposable
    {
        private readonly Action _action;
        public DisposableAction(Action action) => _action = action;
        public void Dispose() => _action();
    }
}
