using Morpheo.Sdk;

namespace Morpheo.Tests.Simulation;

/// <summary>
/// A routing strategy that delegates propagation to the InMemoryNetworkSimulator.
/// Used to hook DataSyncService into the simulation.
/// </summary>
public class SimulatedRoutingStrategy : ISyncRoutingStrategy
{
    private readonly InMemoryNetworkSimulator _simulator;
    private readonly string _localNodeId;

    public SimulatedRoutingStrategy(InMemoryNetworkSimulator simulator, string localNodeId)
    {
        _simulator = simulator;
        _localNodeId = localNodeId;
    }

    public async Task PropagateAsync(
        SyncLogDto log,
        IEnumerable<PeerInfo> candidates,
        Func<PeerInfo, SyncLogDto, Task<bool>> sendFunc)
    {
        // In simulation, we bypass 'candidates' and 'sendFunc' (which use real network),
        // and instead hand off the packet directly to the Simulator Router.
        // The Simulator decides who receives it based on connectivity graph.
        
        await _simulator.RouteMessage(_localNodeId, log);
    }
}
