using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Morpheo.Core.Data;

namespace Morpheo.Core.Sync;

/// <summary>
/// Hybrid Log Store strategy combining LSM File Store (Hot/Write) and SQL Store (Cold/Archive).
/// Ensures infinite retention with high write throughput.
/// </summary>
public class HybridLogStore : ISyncLogStore, IHostedService, IDisposable
{
    private readonly FileLogStore _hotStore;
    private readonly SqlSyncLogStore _coldStore;
    private readonly ILogger<HybridLogStore> _logger;
    private Timer? _archivingTimer;
    private readonly SemaphoreSlim _archivingLock = new(1, 1);

    // Configuration
    private readonly TimeSpan _retentionInHotStore = TimeSpan.FromMinutes(60); // Keep 1h in fast file store
    private readonly TimeSpan _archivingInterval = TimeSpan.FromMinutes(10);   // Check every 10 min

    public HybridLogStore(FileLogStore hotStore, SqlSyncLogStore coldStore, ILogger<HybridLogStore> logger)
    {
        _hotStore = hotStore;
        _coldStore = coldStore;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _hotStore.StartAsync(cancellationToken);
        _archivingTimer = new Timer(async _ => await ArchiveOldLogsAsync(), null, _archivingInterval, _archivingInterval);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _archivingTimer?.Change(Timeout.Infinite, 0);
        await _hotStore.StopAsync(cancellationToken);
    }

    // --- WRITE (Always to Hot Store) ---

    public Task AddLogAsync(SyncLog log)
    {
        return _hotStore.AddLogAsync(log);
    }

    // --- READ (Merge Hot + Cold) ---

    public async Task<List<SyncLog>> GetLogsAsync(long sinceTick, int maxCount = 1000)
    {
        // 1. Parallel Fetch
        var hotTask = _hotStore.GetLogsAsync(sinceTick, maxCount);
        var coldTask = _coldStore.GetLogsAsync(sinceTick, maxCount);

        await Task.WhenAll(hotTask, coldTask);

        var hotLogs = hotTask.Result;
        var coldLogs = coldTask.Result;

        // 2. Merge & Deduplicate
        // Priority to Hot logs if duplication exists (they might be newer updates?)
        // Usually logs are immutable, so dedupe by ID is enough.
        return coldLogs.Concat(hotLogs)
            .DistinctBy(l => l.Id)
            .OrderBy(l => l.Timestamp)
            .Take(maxCount)
            .ToList();
    }

    public async Task<SyncLog?> GetLastLogForEntityAsync(string entityId)
    {
        // Check Hot first (most likely to have recent data)
        var hotLog = await _hotStore.GetLastLogForEntityAsync(entityId);
        
        // If not found or (optionally check if deleted?), check Cold
        // Wait, if Hot has a log, is it the *absolute* latest? 
        // Yes, because Hot contains [Now - 1h ... Now]. Cold contains [0 ... Now - 1h].
        // If hotLog exists, it is by definition newer than anything in Cold.
        if (hotLog != null) return hotLog;

        return await _coldStore.GetLastLogForEntityAsync(entityId);
    }

    public async Task<List<SyncLog>> GetLogsByRangeAsync(long startTick, long endTick)
    {
        // Parallel Fetch
        var hotTask = _hotStore.GetLogsByRangeAsync(startTick, endTick);
        var coldTask = _coldStore.GetLogsByRangeAsync(startTick, endTick);

        await Task.WhenAll(hotTask, coldTask);

        return coldTask.Result.Concat(hotTask.Result)
            .DistinctBy(l => l.Id)
            .OrderBy(l => l.Timestamp)
            .ToList();
    }

    public async Task<int> CountAsync()
    {
        var hotCount = await _hotStore.CountAsync();
        var coldCount = await _coldStore.CountAsync();
        return hotCount + coldCount;
    }

    public async Task<int> DeleteOldLogsAsync(long thresholdTick)
    {
        // Delete strictly from Cold store (Hard Delete)
        // Hot store deletion is handled by Archiving process, not external delete requests usually.
        // But if this is a "Prune EVERYTHING older than X" request (e.g. retention policy 30 days):
        
        var coldDeleted = await _coldStore.DeleteOldLogsAsync(thresholdTick);
        var hotDeleted = await _hotStore.DeleteOldLogsAsync(thresholdTick); // Should be 0 if retention > threshold
        
        return coldDeleted + hotDeleted;
    }

    // --- ARCHIVING PROCESS ---

    private async Task ArchiveOldLogsAsync()
    {
        if (!await _archivingLock.WaitAsync(0)) return;

        try
        {
            var thresholdTime = DateTime.UtcNow.Subtract(_retentionInHotStore);
            // Convert to Ticks? SyncLog uses "long Timestamp".
            // Assuming Timestamp is DateTime.Ticks (standard in Morpheo).
            var thresholdTick = thresholdTime.Ticks;

            _logger.LogDebug("Starting Archiving Process...");

            // 1. Identify candidates in Hot Store
            // We need a chunked read to avoid memory explosion
            // FileLogStore usually stores roughly ordered.
            // Let's get everything older than threshold.
            // WARNING: If there are 1M logs, this loads them all in RAM.
            // Ideally FileLogStore should support "StreamLogsBefore".
            // For now, assume GetLogsByRangeAsync is okay for the 10 min batch.
            
            var logsToArchive = await _hotStore.GetLogsByRangeAsync(0, thresholdTick);
            
            if (logsToArchive.Count == 0) 
            {
                 _logger.LogDebug("No logs to archive.");
                 return;
            }

            _logger.LogInformation($"Archiving {logsToArchive.Count} logs from Hot(LSM) to Cold(SQL)...");

            // 2. Write to Cold Store (Bulk)
            // Batch in chunks of 1000 to avoid SQL timeout
            foreach (var batch in logsToArchive.Chunk(1000))
            {
                await _coldStore.AddLogsBroadcastAsync(batch);
            }

            // 3. Delete from Hot Store
            // FileLogStore DeleteOldLogsAsync works by File Segment.
            // It will only delete files where MaxTick < threshold.
            // This is SAFE. It might leave some logs in Hot Store that are also in Cold Store (overlap).
            // That is acceptable (Hybrid GetLogsAsync handles deduplication).
            
            int deletedFromHot = await _hotStore.DeleteOldLogsAsync(thresholdTick);
            
            _logger.LogInformation($"Archiving Complete. Moved {logsToArchive.Count} logs, Compacted {deletedFromHot} lines from Hot Store.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archiving process failed.");
        }
        finally
        {
            _archivingLock.Release();
        }
    }

    public void Dispose()
    {
        _archivingTimer?.Dispose();
        _archivingLock.Dispose();
        _hotStore.Dispose();
        // ColdStore is typically not disposable (factory used)
    }
}
