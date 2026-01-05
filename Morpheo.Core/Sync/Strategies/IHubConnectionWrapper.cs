using Microsoft.AspNetCore.SignalR.Client;
using Morpheo.Sdk;

namespace Morpheo.Core.Sync.Strategies;

/// <summary>
/// Abstraction for HubConnection to facilitate testing.
/// </summary>
public interface IHubConnectionWrapper
{
    Task StartAsync(CancellationToken token = default);
    Task StopAsync(CancellationToken token = default);
    Task InvokeAsync(string methodName, object? arg1, CancellationToken token = default);
    HubConnectionState State { get; }
    void On<T>(string methodName, Func<T, Task> handler);
    ValueTask DisposeAsync();
}
