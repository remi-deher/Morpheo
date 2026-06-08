using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Core.Data;
using Morpheo.Core.Discovery;
using Morpheo.Core.Security;
using Morpheo.Core.Server;
using Morpheo.Core.Sync;
using Morpheo.Core.Sync.Strategies;
using Morpheo.Sdk;
using Morpheo.Tests.Simulation; // TestEntity + SimpleTypeResolver

namespace Morpheo.Tests.Integration;

/// <summary>
/// End-to-end tests that exercise the real Kestrel server (<see cref="MorpheoWebServer"/>)
/// and the real <see cref="Morpheo.Core.Client.MorpheoHttpClient"/> over loopback HTTP —
/// the path the in-memory simulator bypasses (and where the Cold Sync prod bug once hid).
/// </summary>
public class HttpSyncIntegrationTests : IAsyncLifetime
{
    private readonly List<string> _dbFiles = new();
    private readonly List<Node> _nodes = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var node in _nodes)
            await node.DisposeAsync();
        foreach (var f in _dbFiles)
            try { if (File.Exists(f)) File.Delete(f); } catch { }
    }

    private sealed class Node : IAsyncDisposable
    {
        public required string Name { get; init; }
        public required ServiceProvider Provider { get; init; }
        public required MorpheoWebServer Server { get; init; }
        public required DataSyncService Service { get; init; }
        public required int Port { get; init; }

        public PeerInfo AsPeer() =>
            new(Name, Name, "127.0.0.1", Port, NodeRole.StandardClient, Array.Empty<string>());

        public IMorpheoClient Client => Provider.GetRequiredService<IMorpheoClient>();

        public async ValueTask DisposeAsync()
        {
            await Server.StopAsync(CancellationToken.None);
            await Provider.DisposeAsync();
        }
    }

    private async Task<Node> CreateNodeAsync(string name)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning).AddConsole());

        var options = new MorpheoOptions { NodeName = name, DiscoveryPort = 0 };
        services.AddSingleton(options);

        var dbPath = Path.Combine(Path.GetTempPath(), $"morpheo_it_{name}_{Guid.NewGuid():N}.db");
        _dbFiles.Add(dbPath);
        services.AddDbContext<MorpheoDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

        services.AddSingleton<IEntityTypeResolver>(new SimpleTypeResolver());
        services.AddSingleton<ConflictResolutionEngine>();
        services.AddSingleton<MerkleTreeService>();
        services.AddSingleton<ISyncRoutingStrategy, NullRoutingStrategy>();
        services.AddSingleton<INetworkDiscovery, NullNetworkDiscovery>();
        services.AddSingleton<IRequestAuthenticator, AllowAllAuthenticator>();
        services.AddHttpClient();
        services.AddSingleton<IMorpheoClient, Morpheo.Core.Client.MorpheoHttpClient>();
        services.AddSingleton<DataSyncService>();

        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<MorpheoDbContext>().Database.EnsureCreated();

        var server = new MorpheoWebServer(options, provider);
        await server.StartAsync(CancellationToken.None);

        var node = new Node
        {
            Name = name,
            Provider = provider,
            Server = server,
            Service = provider.GetRequiredService<DataSyncService>(),
            Port = server.LocalPort
        };
        _nodes.Add(node);
        return node;
    }

    private static async Task WriteAsync(Node node, string id) =>
        await node.Service.BroadcastChangeAsync(new TestEntity { Id = id, Content = id }, "UPDATE");

    private static async Task<int> CountAsync(Node node, string entityId)
    {
        using var scope = node.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
        return await db.SyncLogs.CountAsync(l => l.EntityId == entityId);
    }

    // -------------------------------------------------------------------------

    [Fact]
    public async Task Push_ShouldBeReceived_OverRealHttp()
    {
        var a = await CreateNodeAsync("NodeA");
        var b = await CreateNodeAsync("NodeB");

        var dto = new SyncLogDto(
            Guid.NewGuid().ToString(), "push-1", nameof(TestEntity),
            "{\"Id\":\"push-1\",\"Content\":\"hello\"}", "UPDATE",
            DateTime.UtcNow.Ticks, new() { ["NodeA"] = 1 }, "NodeA");

        await a.Client.SendSyncUpdateAsync(b.AsPeer(), dto);

        (await CountAsync(b, "push-1")).Should().Be(1);
    }

    [Fact]
    public async Task History_ShouldRoundTrip_OverRealHttp()
    {
        var a = await CreateNodeAsync("NodeA");
        var b = await CreateNodeAsync("NodeB");

        await WriteAsync(a, "hist-1");
        await WriteAsync(a, "hist-2");

        var page = await b.Client.GetHistoryAsync(a.AsPeer(), 0);

        page.Should().Contain(l => l.EntityId == "hist-1");
        page.Should().Contain(l => l.EntityId == "hist-2");
    }

    [Fact]
    public async Task MerkleEndpoints_ShouldRoundTrip_OverRealHttp()
    {
        var a = await CreateNodeAsync("NodeA");
        var b = await CreateNodeAsync("NodeB");

        await WriteAsync(a, "merkle-1");
        await WriteAsync(a, "merkle-2");

        // Digest computed remotely must equal the source's own digest.
        var remoteDigest = await b.Client.GetDigestAsync(a.AsPeer());
        remoteDigest.Should().NotBeNullOrEmpty();
        remoteDigest.Should().Be(await a.Service.ComputeLocalDigestAsync());

        // Manifest + by-ids round trip returns exactly the requested logs.
        var manifest = await b.Client.GetManifestAsync(a.AsPeer());
        manifest.Should().HaveCount(2);

        var logs = await b.Client.GetLogsByIdsAsync(a.AsPeer(), manifest);
        logs.Should().HaveCount(2);
        logs.Select(l => l.EntityId).Should().BeEquivalentTo(new[] { "merkle-1", "merkle-2" });
    }

    [Fact]
    public async Task HealthEndpoints_ShouldReturnOk()
    {
        var a = await CreateNodeAsync("NodeA");
        using var http = new HttpClient();

        var live = await http.GetAsync($"http://127.0.0.1:{a.Port}/health/live");
        var ready = await http.GetAsync($"http://127.0.0.1:{a.Port}/health/ready");

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
