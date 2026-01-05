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
    /// Requests missing history (Pull) from a target node.
    /// </summary>
    /// <param name="target">The target peer information.</param>
    /// <param name="sinceTick">The tick since when history is requested.</param>
    /// <returns>A list of synchronization logs.</returns>
    Task<List<SyncLogDto>> GetHistoryAsync(PeerInfo target, long sinceTick);
}
