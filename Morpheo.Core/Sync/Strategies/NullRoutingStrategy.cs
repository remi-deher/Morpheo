using Morpheo.Sdk;

namespace Morpheo.Core.Sync.Strategies;

/// <summary>
/// No-op routing strategy.
/// </summary>
public class NullRoutingStrategy : ISyncRoutingStrategy
{
    public Task PropagateAsync(
        SyncLogDto log,
        IEnumerable<PeerInfo> candidates,
        Func<PeerInfo, SyncLogDto, Task<bool>> sendFunc)
    {
        return Task.CompletedTask;
    }
}
