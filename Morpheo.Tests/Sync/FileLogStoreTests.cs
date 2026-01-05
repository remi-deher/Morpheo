using FluentAssertions;
using Morpheo.Core.Sync;
using Morpheo.Sdk;

namespace Morpheo.Tests.Sync;

public class FileLogStoreTests : IDisposable
{
    private readonly string _testDir;

    public FileLogStoreTests()
    {
        // 1. Setup: Unique temporary directory
        _testDir = Path.Combine(Path.GetTempPath(), "MorpheoTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        // Teardown: Cleanup
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch (Exception)
        {
            // Best effort cleanup (files might be locked by OS antivirus etc)
            // In a real CI env we might not care, but good practice.
        }
    }

    [Fact]
    public async Task Persistence_ShouldStartAndCreateManifest()
    {
        // Arrange
        using var store = new FileLogStore(_testDir);

        // Act
        await store.StartAsync();

        // Assert
        var manifestPath = Path.Combine(_testDir, "manifest.json");
        File.Exists(manifestPath).Should().BeTrue();
        
        var json = await File.ReadAllTextAsync(manifestPath);
        json.Should().Contain("Version");
    }

    [Fact]
    public async Task Persistence_ShouldSaveAndReloadLogs_AcrossRestarts()
    {
        // Arrange
        var log = new SyncLogDto(
            Guid.NewGuid().ToString(),
            "entity-1",
            "TestEntity",
            "{}",
            "UPDATE",
            DateTime.UtcNow.Ticks,
            new Dictionary<string, long> { { "NodeA", 1 } },
            "NodeA"
        );

        // Act 1: Write to store
        using (var store1 = new FileLogStore(_testDir))
        {
            await store1.StartAsync();
            await store1.AddLogAsync(log);
            await store1.StopAsync(); // Force flush
        }

        // Act 2: Read from NEW store instance (Simulation of app restart)
        List<SyncLogDto> loadedLogs;
        using (var store2 = new FileLogStore(_testDir))
        {
            await store2.StartAsync();
            loadedLogs = await store2.GetLogsAsync();
        }

        // Assert
        loadedLogs.Should().NotBeNull();
        loadedLogs.Should().HaveCount(1);
        loadedLogs.First().Id.Should().Be(log.Id);
        loadedLogs.First().EntityId.Should().Be("entity-1");
    }
}
