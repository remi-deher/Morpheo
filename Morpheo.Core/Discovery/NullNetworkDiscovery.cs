using Morpheo.Sdk;

namespace Morpheo.Core.Discovery;

/// <summary>
/// No-op implementation of network discovery.
/// Used when discovery is disabled or not supported.
/// </summary>
public class NullNetworkDiscovery : INetworkDiscovery
{
#pragma warning disable CS0067
    public event EventHandler<PeerInfo>? PeerFound;
    public event EventHandler<PeerInfo>? PeerLost;
#pragma warning restore CS0067

    public IReadOnlyList<PeerInfo> GetPeers() => Array.Empty<PeerInfo>();

    public Task StartAdvertisingAsync(PeerInfo localPeer, CancellationToken ct) => Task.CompletedTask;
    public Task StartListeningAsync(CancellationToken ct) => Task.CompletedTask;
    public void Stop() { }
}
