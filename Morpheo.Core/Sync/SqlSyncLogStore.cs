using Microsoft.EntityFrameworkCore;
using Morpheo.Core.Data;

namespace Morpheo.Core.Sync;

/// <summary>
/// Implements a persistent store for synchronization logs using a SQL database via Entity Framework Core.
/// </summary>
public class SqlSyncLogStore
{
    private readonly MorpheoDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlSyncLogStore"/> class.
    /// </summary>
    /// <param name="context">The database context managing connection and access to the SQL store.</param>
    public SqlSyncLogStore(MorpheoDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Asynchronously adds a new synchronization log entry to the database.
    /// </summary>
    /// <param name="log">The synchronization log object to persist.</param>
    /// <returns>A task that represents the asynchronous add operation.</returns>
    /// <remarks>
    /// This method is idempotent; it checks for the existence of a log with the same ID before attempting insertion.
    /// Concurrency exceptions (e.g., duplicate key errors from parallel operations) are caught and ignored if the log already exists.
    /// </remarks>
    public async Task AddLogAsync(SyncLog log)
    {
        try 
        {
            if (await _context.SyncLogs.AnyAsync(l => l.Id == log.Id))
            {
                return;
            }

            _context.SyncLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (await _context.SyncLogs.AnyAsync(l => l.Id == log.Id))
            {
                return;
            }
            throw;
        }
    }

    /// <summary>
    /// Asynchronously retrieves a list of synchronization logs that have a timestamp strictly greater than the specified value.
    /// </summary>
    /// <param name="sinceTimestamp">The reference timestamp. Only logs strictly newer than this value will be returned.</param>
    /// <returns>A task containing a list of <see cref="SyncLog"/> objects ordered by timestamp.</returns>
    public async Task<List<SyncLog>> GetLogsAsync(long sinceTimestamp = 0)
    {
        return await _context.SyncLogs
            .Where(l => l.Timestamp > sinceTimestamp)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();
    }
}
