using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Sync.Strategies;

/// <summary>
/// Strategy for connecting to a SignalR Hub as a client.
/// Relays local changes to the hub and receives remote changes.
/// </summary>
public class SignalRClientStrategy : ISyncStrategyProvider, IDisposable, IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ILogger<SignalRClientStrategy> _logger;

    public SignalRClientStrategy(
        string hubUrl,
        IServiceProvider serviceProvider,
        ILogger<SignalRClientStrategy> logger)
    {
        _logger = logger;

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<SyncLogDto>("ReceiveLog", async (log) =>
        {
            try
            {
                var syncService = serviceProvider.GetRequiredService<DataSyncService>();
                await syncService.ReceiveRemoteLogAsync(log);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing log received from SignalR Hub");
            }
        });

        // Fire and forget start
        _ = ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        try
        {
            await _connection.StartAsync();
            _logger.LogInformation("Connected to SignalR Hub");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to SignalR Hub");
        }
    }

    /// <inheritdoc/>
    public async Task PropagateAsync(
        SyncLogDto log,
        IEnumerable<PeerInfo> candidates,
        Func<PeerInfo, SyncLogDto, Task<bool>> sendFunc)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            try
            {
                await _connection.InvokeAsync("PushLog", log);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push log to SignalR Hub");
            }
        }
    }

    public void Dispose()
    {
        _ = DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }
    }
}
