namespace Morpheo.Core.Server;

/// <summary>
/// Defines the contract for the embedded Morpheo server.
/// </summary>
public interface IMorpheoServer
{
    /// <summary>
    /// Starts the server asynchronously.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Stops the server asynchronously.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task StopAsync(CancellationToken ct);
}
