using Microsoft.AspNetCore.SignalR.Client;
using Morpheo.Sdk;

namespace Morpheo.Core.Sync.Strategies;

/// <summary>
/// Real implementation wrapping the actual HubConnection.
/// </summary>
public class SignalRHubConnectionWrapper : IHubConnectionWrapper
{
    private readonly HubConnection _connection;

    public SignalRHubConnectionWrapper(HubConnection connection)
    {
        _connection = connection;
    }

    public Task StartAsync(CancellationToken token = default) => _connection.StartAsync(token);
    public Task StopAsync(CancellationToken token = default) => _connection.StopAsync(token);
    
    public Task InvokeAsync(string methodName, object? arg1, CancellationToken token = default) 
        => _connection.InvokeAsync(methodName, arg1, token);

    public HubConnectionState State => _connection.State;

    public void On<T>(string methodName, Func<T, Task> handler)
    {
        _connection.On(methodName, handler);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
