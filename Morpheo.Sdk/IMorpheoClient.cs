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
    Task<List<SyncLogDto>> GetHistoryAsync(PeerInfo target, long sinceTick);

    /// <summary>
    /// Requests missing history (Pull) from a target node for a specific time range.
    /// </summary>
    Task<List<SyncLogDto>> GetHistoryByRangeAsync(PeerInfo target, long startTick, long endTick);

    /// <summary>
    /// Gets the root of the Merkle Tree from the target node.
    /// </summary>
    Task<MerkleTreeNode?> GetMerkleRootAsync(PeerInfo target);

    /// <summary>
    /// Gets the children of a specific Merkle Tree node.
    /// </summary>
    Task<List<MerkleTreeNode>> GetMerkleChildrenAsync(PeerInfo target, string nodeHash);

    /// <summary>
    /// Gets a repository (Set) for a specific entity type, enabling local CRUD and reactive updates.
    /// </summary>
    IMorpheoSet<T> Set<T>() where T : MorpheoEntity;
}
