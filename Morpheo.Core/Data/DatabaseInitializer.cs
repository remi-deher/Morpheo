using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Data;

/// <summary>
/// Service responsible for initializing the Morpheo database.
/// Handles migration of legacy databases and creation of new schemas.
/// </summary>
public class DatabaseInitializer
{
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly MorpheoOptions _options;

    public DatabaseInitializer(ILogger<DatabaseInitializer> logger, MorpheoOptions options)
    {
        _logger = logger;
        _options = options;
    }

    /// <summary>
    /// Computes the path to the node-specific database file.
    /// E.g., .../MorpheoData/morpheo_NODE_01.db
    /// </summary>
    /// <returns>The full path to the database file.</returns>
    public string GetDatabasePath()
    {
        var folder = GetDataFolder();

        // Clean the name to avoid invalid characters in filenames
        var cleanName = string.Join("_", _options.NodeName.Split(Path.GetInvalidFileNameChars()));

        return Path.Combine(folder, $"morpheo_{cleanName}.db");
    }

    /// <summary>
    /// Initializes the database asynchronously.
    /// </summary>
    /// <param name="context">The database context.</param>
    public async Task InitializeAsync(MorpheoDbContext context)
    {
        try
        {
            var path = GetDatabasePath();
            _logger.LogInformation($"Initializing Database: {path}");

            // 1. MIGRATION: Check if we need to retrieve a generic legacy database
            MigrateLegacyDatabase(path);

            // 2. CREATION: If the database does not exist, EF Core creates it with the schema
            await context.Database.EnsureCreatedAsync();

            _logger.LogInformation("Database ready.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Critical error during database initialization.");
            throw;
        }
    }

    /// <summary>
    /// Attempts to rename the old 'morpheo.db' file to the new format 'morpheo_NAME.db'
    /// to preserve data during framework update.
    /// </summary>
    /// <param name="targetNewPath">The target path for the new database file.</param>
    private void MigrateLegacyDatabase(string targetNewPath)
    {
        var folder = GetDataFolder();
        var oldLegacyPath = Path.Combine(folder, "morpheo.db");

        // Scenario: The old file exists, but the new one does not yet.
        if (File.Exists(oldLegacyPath) && !File.Exists(targetNewPath))
        {
            try
            {
                _logger.LogWarning("Detected legacy database 'morpheo.db'. Migrating...");

                // Rename the file (Move = Rename)
                File.Move(oldLegacyPath, targetNewPath);

                _logger.LogInformation($"Migration successful! Your data is now in: {Path.GetFileName(targetNewPath)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatic migration failed. A new database will be created.");
            }
        }
    }

    private string GetDataFolder()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MorpheoData");
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }
        return folder;
    }
}
