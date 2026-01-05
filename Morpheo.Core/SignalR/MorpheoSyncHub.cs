using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Morpheo.Core.Sync;
using Morpheo.Sdk;

namespace Morpheo.Core.SignalR;

/// <summary>
/// SignalR Hub for real-time synchronization.
/// </summary>
public class MorpheoSyncHub : Hub
{
    private readonly DataSyncService _dataSyncService;

    public MorpheoSyncHub(DataSyncService dataSyncService)
    {
        _dataSyncService = dataSyncService;
    }

    /// <summary>
    /// Receives a log entry from a client.
    /// </summary>
    /// <param name="log">The synchronization log.</param>
    public async Task PushLog(SyncLogDto log)
    {
        await _dataSyncService.ReceiveRemoteLogAsync(log);
    }
}
