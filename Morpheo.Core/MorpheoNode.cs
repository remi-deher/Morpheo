using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Morpheo.Core.Discovery;
using Morpheo.Core.Server;
using Morpheo.Sdk;

namespace Morpheo.Core;

/// <summary>
/// Main hosted service representing a Morpheo Node.
/// Manages the lifecycle of the web server and network discovery interactions.
/// </summary>
public class MorpheoNode : IHostedService
{
    private readonly MorpheoOptions _options;
    private readonly INetworkDiscovery _discovery;
    private readonly IMorpheoServer _server;
    private readonly ILogger<MorpheoNode> _logger;

    public MorpheoNode(
        MorpheoOptions options,
        INetworkDiscovery discovery,
        IMorpheoServer server,
        ILogger<MorpheoNode> logger)
    {
        _options = options;
        _discovery = discovery;
        _server = server;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Morpheo Node: {NodeName} (Port {Port})", _options.NodeName, _options.DiscoveryPort);

        // 1. Start Web Server (to receive syncs)
        await _server.StartAsync(cancellationToken);

        // 2. Start Discovery (to find peers)
        var peerInfo = new PeerInfo(
            _options.NodeName,
            _options.NodeName,
            "0.0.0.0", // Will be replaced by actual IP
            _options.DiscoveryPort,
            NodeRole.StandardClient,
            Array.Empty<string>()
        );

        await _discovery.StartListeningAsync(cancellationToken);
        await _discovery.StartAdvertisingAsync(peerInfo, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Morpheo Node...");
        _discovery.Stop();
        await _server.StopAsync(cancellationToken);
    }
}
