namespace Morpheo.Sdk;

/// <summary>
/// Definition of a client capable of communicating with other Morpheo nodes.
/// </summary>
public interface IMorpheoClient
{
    /// <summary>
    /// Sends a print job to a target node asynchronously (Fire and Forget).
    /// </summary>
    /// <param name="target">The target peer information.</param>
    /// <param name="content">The content to print.</param>
    /// <param name="printerName">The target printer name.</param>
    Task SendPrintJobAsync(PeerInfo target, string content, string printerName = "Virtual-Zebra-01");

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

    /// <summary>
    /// Retrieves the peer's Merkle root digest over its set of log Ids, for a fast
    /// equality check during anti-entropy reconciliation.
    /// </summary>
    /// <param name="target">The target peer information.</param>
    /// <returns>The peer's root hash, or an empty string if it has no logs/unavailable.</returns>
    Task<string> GetDigestAsync(PeerInfo target);

    /// <summary>
    /// Retrieves the peer's reconciliation manifest — the full set of log Ids it holds.
    /// </summary>
    /// <param name="target">The target peer information.</param>
    Task<List<string>> GetManifestAsync(PeerInfo target);

    /// <summary>
    /// Pulls the specific logs identified by <paramref name="ids"/> from the peer.
    /// </summary>
    /// <param name="target">The target peer information.</param>
    /// <param name="ids">The log Ids to fetch.</param>
    Task<List<SyncLogDto>> GetLogsByIdsAsync(PeerInfo target, IReadOnlyList<string> ids);
}
