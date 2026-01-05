using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Morpheo.Sdk;
using Morpheo.Core.Sync;

namespace Morpheo.Core.Data;

/// <summary>
/// Represents a synchronization log entry stored in the database.
/// </summary>
[Table("MorpheoSyncLogs")]
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

    // Storage for the Logical Clock State (Vector or HLC)
    // Stored as a serialized string (JSON or "pt:lc")
    public string ClockState { get; set; } = "{}";
    
    public SyncPriority Priority { get; set; } = SyncPriority.Normal;
    
    // Integrity check for Patches
    public string? BaseContentHash { get; set; }
}

/// <summary>
/// The Entity Framework Core context for Morpheo data.
/// </summary>
public class MorpheoDbContext : DbContext
{
    // Hybrid Storage: SQL is used as "Cold Store"
    public DbSet<SyncLog> SyncLogs { get; set; }

    public MorpheoDbContext() { }

    public MorpheoDbContext(DbContextOptions<MorpheoDbContext> options) : base(options) { }

    protected MorpheoDbContext(DbContextOptions options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<SyncLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp); // Fast range queries
            entity.HasIndex(e => e.EntityId);  // Fast history lookup
            entity.Property(e => e.JsonData).HasColumnType("TEXT"); // Force unlimited text
        });
    }
}
