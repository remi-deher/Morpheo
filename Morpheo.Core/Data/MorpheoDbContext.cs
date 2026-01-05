using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Morpheo.Sdk;
using Morpheo.Core.Sync;

namespace Morpheo.Core.Data;

/// <summary>
/// Represents a synchronization log entry stored in the database.
/// </summary>
public class SyncLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EntityId { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string JsonData { get; set; } = "{}";
    public string Action { get; set; } = "UPDATE";
    public long Timestamp { get; set; }
    public bool IsFromRemote { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    // Storage for the Vector Clock

    // Stored as JSON string in the database
    public string VectorClockJson { get; set; } = "{}";

    // Used in code (Computed, not stored)
    [NotMapped]
    public VectorClock Vector
    {
        get => VectorClock.FromJson(VectorClockJson);
        set => VectorClockJson = value.ToJson();
    }
}

/// <summary>
/// The Entity Framework Core context for Morpheo data.
/// </summary>
public class MorpheoDbContext : DbContext
{
    public DbSet<SyncLog> SyncLogs { get; set; }

    // Remote constructor required for some tools
    public MorpheoDbContext() { }

    // Updated constructor to be generic
    public MorpheoDbContext(DbContextOptions<MorpheoDbContext> options) : base(options) { }

    protected MorpheoDbContext(DbContextOptions options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<SyncLog>().HasKey(l => l.Id);

        // Index to speed up entity lookup
        modelBuilder.Entity<SyncLog>().HasIndex(l => l.EntityId);
    }
}
