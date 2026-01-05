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
        // Broadcast to all connected clients
        await _hubContext.Clients.All.SendAsync("ReceiveLog", log);
    }
}
