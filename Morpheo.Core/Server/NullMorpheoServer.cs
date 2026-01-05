using Morpheo.Sdk;

namespace Morpheo.Core.Server;

/// <summary>
/// No-op server implementation.
/// </summary>
public class NullMorpheoServer : IMorpheoServer
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
