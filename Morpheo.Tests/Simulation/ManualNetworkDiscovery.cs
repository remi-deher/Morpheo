using Morpheo.Sdk;

namespace Morpheo.Tests.Simulation;

public class ManualNetworkDiscovery : INetworkDiscovery
{
    public event EventHandler<PeerInfo>? PeerFound;
    public event EventHandler<PeerInfo>? PeerLost;

    public Task StartAdvertisingAsync(PeerInfo myInfo, CancellationToken ct) => Task.CompletedTask;
    public Task StartListeningAsync(CancellationToken ct) => Task.CompletedTask;
    public void Stop() { }
    
    public IReadOnlyList<PeerInfo> GetPeers() => new List<PeerInfo>();

    public void SimulatePeerFound(PeerInfo peer)
    {
        PeerFound?.Invoke(this, peer);
    }
}
