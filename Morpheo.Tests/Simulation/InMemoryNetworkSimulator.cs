using System.Collections.Concurrent;
using Morpheo.Core.Sync;
using Morpheo.Sdk;

namespace Morpheo.Tests.Simulation;

/// <summary>
/// A simulated in-memory network router for integration testing.
/// Manages virtual connections and message passing between DataSyncService instances.
/// </summary>
public class InMemoryNetworkSimulator
{
    private readonly ConcurrentDictionary<string, DataSyncService> _participants = new();
    private readonly ConcurrentDictionary<string, IServiceProvider> _providers = new(); // NEW: Store providers to access DBs
    private readonly ConcurrentDictionary<string, bool> _disconnectedNodes = new();

    public DataSyncService? GetService(string nodeId) => _participants.TryGetValue(nodeId, out var s) ? s : null;
    public IServiceProvider? GetProvider(string nodeId) => _providers.TryGetValue(nodeId, out var p) ? p : null;
    
    // Key: NodeId, Value: True if isolated (cannot send OR receive)
    
    public void Register(string nodeId, DataSyncService service, IServiceProvider provider)
    {
        _participants[nodeId] = service;
        _providers[nodeId] = provider;
        _disconnectedNodes[nodeId] = false;
    }
    
    public void Disconnect(string nodeId)
    {
        if (_participants.ContainsKey(nodeId))
        {
            _disconnectedNodes[nodeId] = true;
        }
    }

    public void Reconnect(string nodeId)
    {
        if (_participants.ContainsKey(nodeId))
        {
            _disconnectedNodes[nodeId] = false;
        }
    }
    
    // Isolate is effectively Disconnect in this simple model, 
    // but semantically we treat it as "Network Partition".
    // Disconnect = Can't send/receive.
    public void Isolate(string nodeId) => Disconnect(nodeId);

    /// <summary>
    /// Routes a message from sender to all other participants, simulating network travel.
    /// </summary>
    public async Task RouteMessage(string senderId, SyncLogDto log)
    {
        if (_disconnectedNodes.TryGetValue(senderId, out var isolated) && isolated)
        {
            // Sender is isolated/disconnected: cannot send.
            return;
        }

        foreach (var receiverId in _participants.Keys)
        {
            if (receiverId == senderId) continue;
            
            if (_disconnectedNodes.TryGetValue(receiverId, out var receiverIsolated) && receiverIsolated)
            {
                // Receiver is isolated: cannot receive.
                continue;
            }

            if (_participants.TryGetValue(receiverId, out var service))
            {
                // Simulate network latency?
                // await Task.Delay(10); 
                
                // Deliver message
                await service.ReceiveRemoteLogAsync(log);
            }
        }
    }
}
