namespace Morpheo.Sdk;

/// <summary>
/// Definition of a client capable of communicating with other Morpheo nodes.
/// </summary>
public interface IMorpheoClient
{
    /// <summary>
    /// Sends a print job to a target node asynchronously (Fire & Forget).
    /// </summary>
    /// <param name="target">The target peer information.</param>
    /// <param name="content">The content to print.</param>
    Task SendPrintJobAsync(PeerInfo target, string content);

    /// <summary>
    /// Sends a data synchronization update (Push) to a target node.
    /// </summary>
    /// <param name="target">The target peer information.</param>
    /// <param name="log">The synchronization log entry.</param>
    Task SendSyncUpdateAsync(PeerInfo target, SyncLogDto log);

    /// <summary>
    /// Sends multiple synchronization updates to a target node in a single request.
    /// Reduces network round-trips when many entities change in a short window.
    /// </summary>
    /// <param name="target">The target peer information.</param>
    /// <param name="logs">The synchronization log entries to push together.</param>
    /// <returns><c>true</c> if the batch was accepted by the peer; otherwise <c>false</c>.</returns>
    Task<bool> SendSyncBatchAsync(PeerInfo target, IReadOnlyList<SyncLogDto> logs);

    /// <summary>
    /// Requests missing history (Pull) from a target node.
    /// </summary>
    /// <param name="target">The target peer information.</param>
    /// <param name="sinceTick">The tick since when history is requested.</param>
    /// <param name="limit">Maximum number of logs to return in this page.</param>
    /// <returns>A single page of synchronization logs, ordered chronologically.</returns>
    Task<List<SyncLogDto>> GetHistoryAsync(PeerInfo target, long sinceTick, int limit = 500);
}
