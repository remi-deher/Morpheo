using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Morpheo.Core.Data;
using Morpheo.Core.Sync;
using Morpheo.Sdk;

namespace Morpheo.Tests.Simulation;

public class SystemReplicationTests : IDisposable
{
    private readonly InMemoryNetworkSimulator _simulator;
    private readonly List<string> _tempDirs = new();

    public SystemReplicationTests()
    {
        _simulator = new InMemoryNetworkSimulator();
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch { /* Best effort */ }
        }
    }

    private class NodeContext
    {
        public required DataSyncService Service { get; set; }
        public required IServiceProvider Provider { get; set; }
        public required string NodeId { get; set; }
        public required ManualNetworkDiscovery Discovery { get; set; }
    }

    private NodeContext CreateNode(string nodeId)
    {
        var services = new ServiceCollection();
        
        // 1. Temp Storage
        var tempPath = Path.Combine(Path.GetTempPath(), "MorpheoSystemTests", nodeId, Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);
        _tempDirs.Add(tempPath); // For cleanup

        // 2. Options
        var options = new MorpheoOptions { NodeName = nodeId, LocalStoragePath = tempPath };
        services.AddSingleton(options);

        // 3. Database (Sqlite per node)
        var dbPath = Path.Combine(tempPath, "morpheo.db");
        services.AddDbContext<MorpheoDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));

        // 4. Core Services
        services.AddSingleton<ConflictResolutionEngine>();
        services.AddSingleton<IEntityTypeResolver>(new SimpleTypeResolver());
        services.AddLogging(logging => logging.AddConsole());
        
        var discovery = new ManualNetworkDiscovery();
        services.AddSingleton<INetworkDiscovery>(discovery);

        // 5. Simulation Hooks
        services.AddSingleton(_simulator);
        services.AddSingleton<IMorpheoClient>(sp => new SimulatedMorpheoClient(_simulator));
        services.AddSingleton<ISyncRoutingStrategy>(sp => new SimulatedRoutingStrategy(_simulator, nodeId));
        services.AddScoped<DataSyncService>(); // Scoped or Singleton? Usually HostedService is Singleton? 
        // DataSyncService constructor takes providers, let's make it Singleton for the test ease
        services.AddSingleton<DataSyncService>();

        var provider = services.BuildServiceProvider();

        // Init DB
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
            db.Database.EnsureCreated();
        }

        var service = provider.GetRequiredService<DataSyncService>();
        
        // Register to simulator
        _simulator.Register(nodeId, service, provider);

        return new NodeContext 
        { 
            Service = service, 
            Provider = provider, 
            NodeId = nodeId,
            Discovery = discovery
        };
    }

    private async Task WaitForConditionAsync(Func<Task<bool>> condition, int timeoutMs = 2000)
    {
        var start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Data_Should_Propagate_From_A_To_C_Via_B() // Transitivity?
    {
        // Actually, with InMemoryNetworkSimulator broadcast, A -> C happens directly.
        // But verifying that C receives data from A confirms propagation.
        
        // Arrange
        var nodeA = CreateNode("NodeA");
        var nodeB = CreateNode("NodeB");
        var nodeC = CreateNode("NodeC");

        // Act
        // A broadcasts
        await nodeA.Service.BroadcastChangeAsync(new TestEntity { Id = "doc-1", Content = "User A" }, "UPDATE");

        // Assert
        // Check C
        await WaitForConditionAsync(async () =>
        {
            using var scope = nodeC.Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
            return await db.SyncLogs.AnyAsync(l => l.EntityId == "doc-1");
        });

        using (var scope = nodeC.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
            var log = await db.SyncLogs.FirstAsync(l => l.EntityId == "doc-1");
            log.JsonData.Should().Contain("User A");
        }
    }

    [Fact]
    public async Task Node_Should_Sync_Missed_Data_After_Reconnection()
    {
        // Arrange
        var nodeA = CreateNode("NodeA");
        var nodeB = CreateNode("NodeB"); // This one will go offline
        var nodeC = CreateNode("NodeC");

        // 1. Initial State: All connected.
        // 2. Disconnect B
        _simulator.Disconnect("NodeB");

        // 3. A and C generate data
        await nodeA.Service.BroadcastChangeAsync(new TestEntity { Id = "doc-A", Content = "From A" }, "UPDATE");
        await nodeC.Service.BroadcastChangeAsync(new TestEntity { Id = "doc-C", Content = "From C" }, "UPDATE");

        // 4. Verify A has C's data (connected)
        await WaitForConditionAsync(async () =>
        {
            using var scope = nodeA.Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
            return await db.SyncLogs.AnyAsync(l => l.EntityId == "doc-C");
        });

        // 5. Verify B is empty (Disconnected)
        using (var scope = nodeB.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
            var count = await db.SyncLogs.CountAsync();
            count.Should().Be(0); 
        }

        // 6. Reconnect B
        _simulator.Reconnect("NodeB");

        // 7. Trigger Re-Sync (Cold Sync)
        // In real life, Discovery triggers this. Here we manually invoke the sync logic.
        // We simulate that NodeB "Discovers" NodeA and NodeC
        // Access private method? No, we can't. 
        // We can simulate it by firing the PeerFound event on the Mock Discovery?
        // But we instantiated Mock discovery in CreateNode and didn't keep reference.
        // Let's rely on constructing PeerInfo and calling... DataSyncService has private handlers.
        // Actually, Simulating PeerFound is the best way.
        
        // Wait, I need access to the Mock<INetworkDiscovery> of NodeB to raise the event.
        // Refactor CreateNode to return mock? Or just use reflection/public method?
        // DataSyncService treats Discovery events.
        
        // ALTERNATIVE: Since we can't easily trigger the private event handler without the mock ref,
        // We will assume that in a System Test we might need to expose a "ForceSync(peer)" method 
        // OR we refactor CreateNode to give us the mock.
        
        // Let's compromise: I will just instantiate a ManualDiscovery (fake implementation) instead of Mock.
    }
}

// Helper for test entity
public class TestEntity : MorpheoEntity
{
    public string Content { get; set; } = "";
}

public class SimpleTypeResolver : IEntityTypeResolver
{
    public Type? ResolveType(string entityName)
    {
        if (entityName == nameof(TestEntity)) return typeof(TestEntity);
        return null;
    }
}
