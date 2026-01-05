using Morpheo.Sdk;

namespace Morpheo.Core.Sync.Strategies;

/// <summary>
/// No-op strategy provider.
/// </summary>
public class NullStrategyProvider : ISyncStrategyProvider
{
    public Task PropagateAsync(
        SyncLogDto log,
        IEnumerable<PeerInfo> candidates,
        Func<PeerInfo, SyncLogDto, Task<bool>> sendFunc)
    {
        return Task.CompletedTask;
    }
}
