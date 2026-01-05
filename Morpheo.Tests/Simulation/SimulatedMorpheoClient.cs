using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Morpheo.Core.Data;
using Morpheo.Sdk;

namespace Morpheo.Tests.Simulation;

public class SimulatedMorpheoClient : IMorpheoClient
{
    private readonly InMemoryNetworkSimulator _simulator;

    public SimulatedMorpheoClient(InMemoryNetworkSimulator simulator)
    {
        _simulator = simulator;
    }

    public Task SendPrintJobAsync(PeerInfo target, string content) => Task.CompletedTask;

    public Task SendSyncUpdateAsync(PeerInfo target, SyncLogDto log)
    {
        // This is primarily used by the strategy, but in our Simulation Strategy we bypass this.
        // However, if the code calls this directly, we could route it.
        // For SystemTests using SimulatedRoutingStrategy, this might not be called.
        return Task.CompletedTask;
    }

    public async Task<List<SyncLogDto>> GetHistoryAsync(PeerInfo target, long sinceTick)
    {
        // Cold Sync Simulation:
        // 1. Find the target node in the simulator.
        // 2. Access its DB directly to fetch logs > sinceTick.
        
        var provider = _simulator.GetProvider(target.Id); // Assuming PeerInfo.Id matches NodeId
        if (provider == null) return new List<SyncLogDto>(); // Node not found or disconnected simulation logic?

        // Check if TARGET is disconnected? If so, we can't fetch.
        // Check if WE are disconnected? 
        // For simplicity, let's assume if we can call this, we are connected, but we should assert target is reachable.
        // (Simulator doesn't expose IsConnected checker publicly easily, let's assume success if registered)
        
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
        
        var logs = await db.SyncLogs
            .Where(l => l.Timestamp > sinceTick)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();

        return logs.Select(l => new SyncLogDto(
            l.Id,
            l.EntityId,
            l.EntityName,
            l.JsonData,
            l.Action,
            l.Timestamp,
            l.Vector,
            "HistorySource" // Simplified
        )).ToList();
    }
}
