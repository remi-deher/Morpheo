using Microsoft.EntityFrameworkCore;
using Morpheo.Core.Data;

namespace Morpheo.Core.Sync;

/// <summary>
/// SQL-based implementation of SyncLogStore.
/// Used as "Cold Store" in the Hybrid Architecture.
/// </summary>
public class SqlSyncLogStore : ISyncLogStore
{
    private readonly IDbContextFactory<MorpheoDbContext> _contextFactory;

    public SqlSyncLogStore(IDbContextFactory<MorpheoDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task AddLogAsync(SyncLog log)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        context.SyncLogs.Add(log);
        await context.SaveChangesAsync();
    }

    public async Task<int> CountAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.SyncLogs.CountAsync();
    }

    public async Task<int> DeleteOldLogsAsync(long thresholdTick)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var oldLogs = context.SyncLogs.Where(l => l.Timestamp < thresholdTick);
        context.SyncLogs.RemoveRange(oldLogs); // EF Core 7+ executes delete directly usually, but standard remove is safer for providers
        // Actually ExecuteDeleteAsync is better in EF Core 7+
        return await oldLogs.ExecuteDeleteAsync();
    }

    public async Task<SyncLog?> GetLastLogForEntityAsync(string entityId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.SyncLogs
            .Where(l => l.EntityId == entityId)
            .OrderByDescending(l => l.Timestamp)
            .FirstOrDefaultAsync();
    }

    public async Task<List<SyncLog>> GetLogsAsync(long sinceTick, int maxCount = 1000)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.SyncLogs
            .AsNoTracking()
            .Where(l => l.Timestamp > sinceTick)
            .OrderBy(l => l.Timestamp)
            .Take(maxCount)
            .ToListAsync();
    }

    public async Task<List<SyncLog>> GetLogsByRangeAsync(long startTick, long endTick)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.SyncLogs
            .AsNoTracking()
            .Where(l => l.Timestamp >= startTick && l.Timestamp <= endTick)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();
    }

    // Bulk insert helper for archiving
    public async Task AddLogsBroadcastAsync(IEnumerable<SyncLog> logs)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        await context.SyncLogs.AddRangeAsync(logs);
        await context.SaveChangesAsync();
    }
}
