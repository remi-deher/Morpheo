using Morpheo.Sdk;

namespace Morpheo.Core.Sync.Strategies;

/// <summary>
/// Default strategy: Floods the network (Gossip).
/// Ideal for pure Mesh mode without a central server.
/// </summary>
public class MeshBroadcastStrategy : ISyncStrategyProvider
{
    /// <inheritdoc/>
    public async Task PropagateAsync(
        SyncLogDto log,
        IEnumerable<PeerInfo> candidates,
        Func<PeerInfo, SyncLogDto, Task<bool>> sendFunc)
    {
        // Broadcast to everyone in parallel
        var tasks = candidates.Select(peer => sendFunc(peer, log));
        await Task.WhenAll(tasks);
    }
}
