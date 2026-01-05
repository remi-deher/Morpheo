using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Discovery;

/// <summary>
/// UDP-based implementation of network discovery.
/// Broadcasts "Hello" packets and listens for responses to find peers on the local network.
/// </summary>
public class UdpDiscoveryService : INetworkDiscovery, IDisposable
{
    private readonly MorpheoOptions _options;
    private readonly ILogger<UdpDiscoveryService> _logger;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;

    // Background tasks tracking
    private Task? _receiveTask;
    private Task? _broadcastTask;
    private Task? _cleanupTask;

    private readonly ConcurrentDictionary<string, (PeerInfo Info, DateTime LastSeen)> _peers = new();

    public event EventHandler<PeerInfo>? PeerFound;
    public event EventHandler<PeerInfo>? PeerLost;

    public UdpDiscoveryService(MorpheoOptions options, ILogger<UdpDiscoveryService> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public IReadOnlyList<PeerInfo> GetPeers()
    {
        return _peers.Values.Select(x => x.Info).ToList();
    }

    // Unique Socket Initialization
    private void EnsureSocketInitialized()
    {
        if (_udpClient != null) return;

        _udpClient = new UdpClient();
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _options.DiscoveryPort));
        _udpClient.EnableBroadcast = true;

        _cts = new CancellationTokenSource();

        _logger.LogInformation($"UDP Socket opened on port {_options.DiscoveryPort}");
    }

    /// <inheritdoc/>
    public Task StartAdvertisingAsync(PeerInfo myInfo, CancellationToken ct)
    {
        EnsureSocketInitialized();

        // Link external token with internal CTS to allow Stop()
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts!.Token, ct);

        // Start background task (Fire and Forget)
        _broadcastTask = BroadcastLoopAsync(myInfo, linkedCts.Token);

        // Start cleanup task here as well
        _cleanupTask = CleanupLoopAsync(linkedCts.Token);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StartListeningAsync(CancellationToken ct)
    {
        EnsureSocketInitialized();

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts!.Token, ct);

        // Start listening in background
        _receiveTask = ReceiveLoopAsync(linkedCts.Token);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Stop()
    {
        _logger.LogInformation("Stopping Discovery Service...");
        _cts?.Cancel(); // Cancel all loops
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        _logger.LogDebug("Starting UDP Listening");
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_udpClient == null) break;

                // ReceiveAsync doesn't accept a token directly before .NET 6/7, 
                // but we can catch the error on close.
                var result = await _udpClient.ReceiveAsync(token);
                var packet = DiscoveryPacket.Deserialize(result.Buffer);

                if (packet == null) continue;
                if (packet.Name == _options.NodeName) continue;

                HandleIncomingPacket(packet, result.RemoteEndPoint.Address.ToString());
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning($"Ignored UDP packet: {ex.Message}");
            }
        }
    }

    private async Task BroadcastLoopAsync(PeerInfo myInfo, CancellationToken token)
    {
        _logger.LogDebug("Starting UDP Broadcast");
        var endpoint = new IPEndPoint(IPAddress.Broadcast, _options.DiscoveryPort);

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_udpClient == null) break;

                var packet = new DiscoveryPacket
                {
                    Id = myInfo.Id,
                    Name = myInfo.Name,
                    Role = myInfo.Role,
                    IpAddress = myInfo.IpAddress, // Often "0.0.0.0", receiver deduces real IP
                    Port = myInfo.Port,
                    Tags = myInfo.Tags,
                    Type = DiscoveryMessageType.Hello
                };

                var data = DiscoveryPacket.Serialize(packet);
                await _udpClient.SendAsync(data, data.Length, endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Broadcast Error: {ex.Message}");
            }

            try { await Task.Delay(_options.DiscoveryInterval, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CleanupLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var timeout = _options.DiscoveryInterval * 3 + TimeSpan.FromSeconds(2);

            foreach (var peer in _peers)
            {
                if (now - peer.Value.LastSeen > timeout)
                {
                    if (_peers.TryRemove(peer.Key, out var removed))
                    {
                        _logger.LogInformation($"Peer lost (Timeout): {removed.Info.Name}");
                        PeerLost?.Invoke(this, removed.Info);
                    }
                }
            }

            try { await Task.Delay(5000, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void HandleIncomingPacket(DiscoveryPacket packet, string realIp)
    {
        // Use IP detected by socket (realIp) because packet.IpAddress is often generic
        var info = new PeerInfo(packet.Id, packet.Name, realIp, packet.Port, packet.Role, packet.Tags);

        if (packet.Type == DiscoveryMessageType.Bye)
        {
            if (_peers.TryRemove(packet.Id, out _)) PeerLost?.Invoke(this, info);
            return;
        }

        bool isNew = !_peers.ContainsKey(packet.Id);
        _peers[packet.Id] = (info, DateTime.UtcNow);

        if (isNew)
        {
            _logger.LogInformation($"Peer found: {info.Name} @ {realIp}:{info.Port}");
            PeerFound?.Invoke(this, info);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
