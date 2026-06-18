using Microsoft.AspNetCore.SignalR;
using Morpheo.Core.SignalR;
using Morpheo.Sdk;

namespace Morpheo.Core.Sync.Strategies;

/// <summary>
/// Strategy for broadcasting changes to all connected SignalR clients (Server-side).
/// </summary>
public class SignalRServerBroadcastStrategy : ISyncStrategyProvider
{
    private readonly IHubContext<MorpheoSyncHub> _hubContext;

    public SignalRServerBroadcastStrategy(IHubContext<MorpheoSyncHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc/>
    public async Task PropagateAsync(
        SyncLogDto log,
        IEnumerable<PeerInfo> candidates,
        Func<PeerInfo, SyncLogDto, Task<bool>> sendFunc)
    {
        // Broadcast to all connected SignalR clients (server-side announcement)
        // This complements the sendFunc which routes to specific P2P/HTTP peers
        var hubContext = SignalRHubContextLocator.HubContext ?? _hubContext;
        await hubContext.Clients.All.SendAsync("ReceiveLog", log);

        // Additionally, propagate to explicitly targeted peers via the routing strategy
        var peersArray = candidates.ToArray();
        if (peersArray.Length > 0)
        {
            var tasks = peersArray.Select(peer => sendFunc(peer, log));
            await Task.WhenAll(tasks);
        }
    }
}

/// <summary>
/// Service locator to bridge parent DI container and child DI container for SignalR server hub context.
/// </summary>
public static class SignalRHubContextLocator
{
    public static IHubContext<MorpheoSyncHub>? HubContext { get; set; }
}
