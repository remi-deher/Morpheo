using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Morpheo.Core.Data;
using Morpheo.Core.Sync;

namespace Morpheo.Tests.Sync;

public class SqlSyncLogStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MorpheoDbContext _context;
    private readonly SqlSyncLogStore _store;

    public SqlSyncLogStoreTests()
    {
        // 1. Setup SQLite In-Memory
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<MorpheoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new MorpheoDbContext(options);
        _context.Database.EnsureCreated();

        _store = new SqlSyncLogStore(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Add_And_Get_ShouldPersistAndRetrieveData()
    {
        // Arrange
        var log = new SyncLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = "e1",
            EntityName = "TestEntity",
            JsonData = "{\"foo\":\"bar\"}",
            Timestamp = 100,
            Action = "UPDATE"
        };

        // Act
        await _store.AddLogAsync(log);
        var logs = await _store.GetLogsAsync();

        // Assert
        logs.Should().HaveCount(1);
        var retrieved = logs.First();
        retrieved.Id.Should().Be(log.Id);
        retrieved.EntityId.Should().Be("e1");
        retrieved.Timestamp.Should().Be(100);
        retrieved.JsonData.Should().Be(log.JsonData);
    }

    [Fact]
    public async Task Idempotency_ShouldHandleDuplicateInsertsGracefully()
    {
        // Arrange
        var log = new SyncLog
        {
            Id = "fixed-id",
            EntityId = "e1",
            Timestamp = 100
        };

        // Act
        await _store.AddLogAsync(log);
        
        // Try adding again
        Func<Task> act = async () => await _store.AddLogAsync(log);

        // Assert
        await act.Should().NotThrowAsync();
        
        var logs = await _store.GetLogsAsync();
        logs.Should().HaveCount(1); // Should still be 1
    }

    [Fact]
    public async Task Filtering_ShouldReturnOnlyNewerLogs()
    {
        // Arrange
        await _store.AddLogAsync(new SyncLog { Id = "1", Timestamp = 10 });
        await _store.AddLogAsync(new SyncLog { Id = "2", Timestamp = 20 });
        await _store.AddLogAsync(new SyncLog { Id = "3", Timestamp = 30 });

        // Act
        var logs = await _store.GetLogsAsync(15);

        // Assert
        logs.Should().HaveCount(2);
        logs.Select(l => l.Timestamp).Should().ContainInOrder(20, 30);
    }
}
