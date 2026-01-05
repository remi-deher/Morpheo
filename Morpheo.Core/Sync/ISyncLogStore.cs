using Morpheo.Core.Data;
using Morpheo.Sdk; // For SyncLogDto alias if needed, though SyncLog is in Core.Data

namespace Morpheo.Core.Sync;

/// <summary>
/// Abstraction for storing and retrieving synchronization logs.
/// Decouples the Sync engine from specific storage (EF Core vs LSM).
/// </summary>
public interface ISyncLogStore
{
    /// <summary>
    /// Persists a new log entry.
    /// </summary>
    Task AddLogAsync(SyncLog log);

    /// <summary>
    /// Retrieves a batch of logs starting from a specific timestamp.
    /// </summary>
    Task<List<SyncLog>> GetLogsAsync(long sinceTick, int maxCount = 1000);

    /// <summary>
    /// Retrieves the total count of logs.
    /// </summary>
    Task<int> CountAsync();

    /// <summary>
    /// Gets the most recent log for a specific entity (used for conflict resolution context).
    /// </summary>
    Task<SyncLog?> GetLastLogForEntityAsync(string entityId);

    /// <summary>
    /// Retrieves logs within a specific time range.
    /// </summary>
    Task<List<SyncLog>> GetLogsByRangeAsync(long startTick, long endTick);
    /// <summary>
    /// Deletes logs older than the specified timestamp (Compaction).
    /// </summary>
    Task<int> DeleteOldLogsAsync(long thresholdTick);
}
