namespace Morpheo.Sdk;

/// <summary>
/// Defines the contract for network discovery services.
/// </summary>
public interface INetworkDiscovery
{
    /// <summary>
    /// Starts broadcasting presence (Hello packets) in the background.
    /// This method should not block execution.
    /// </summary>
    /// <param name="myInfo">The local peer information.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StartAdvertisingAsync(PeerInfo myInfo, CancellationToken ct);

    /// <summary>
    /// Starts listening for incoming packets in the background.
    /// This method should not block execution.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task StartListeningAsync(CancellationToken ct);

    /// <summary>
    /// Stops the discovery service and releases resources (sockets).
    /// </summary>
    void Stop();

    /// <summary>
    /// Event triggered when a new peer is found.
    /// </summary>
    event EventHandler<PeerInfo> PeerFound;

    /// <summary>
    /// Event triggered when a peer is lost.
    /// </summary>
    event EventHandler<PeerInfo> PeerLost;

    /// <summary>
    /// Retrieves the list of currently known peers.
    /// </summary>
    /// <returns>A read-only list of peers.</returns>
    IReadOnlyList<PeerInfo> GetPeers();
}
