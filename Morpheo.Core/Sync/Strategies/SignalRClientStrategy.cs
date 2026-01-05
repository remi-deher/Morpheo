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
    private readonly IHubConnectionWrapper _connection;
    private readonly ILogger<SignalRClientStrategy> _logger;

    // Production constructor
    public SignalRClientStrategy(
        string hubUrl,
        IServiceProvider serviceProvider,
        ILogger<SignalRClientStrategy> logger) 
        : this(new SignalRHubConnectionWrapper(new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build()), serviceProvider, logger)
    {
    }

    // Testable constructor
    public SignalRClientStrategy(
        IHubConnectionWrapper connection,
        IServiceProvider serviceProvider,
        ILogger<SignalRClientStrategy> logger)
    {
        _connection = connection;
        _logger = logger;

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
        // Wrapper doesn't expose HubConnectionState enum directly if we didn't map it,
        // but we did map it in interface.
        // Assuming IHubConnectionWrapper.State returns HubConnectionState or equivalent.
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
