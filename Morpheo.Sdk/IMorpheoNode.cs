namespace Morpheo.Sdk;

/// <summary>
/// Represents a Morpheo node causing the initialization and management of services (Database, Network, etc.).
/// </summary>
public interface IMorpheoNode
{
    /// <summary>
    /// Starts the node's services (Database + Network).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops the node gracefully.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Gets the network discovery layer to subscribe to PeerFound/PeerLost events.
    /// </summary>
    INetworkDiscovery Discovery { get; }
}
