using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Morpheo.Core.Data;

/// <summary>
/// Hosted service responsible for initializing the Morpheo database at startup.
/// Ensures that migrations and schema creation happen before the application starts serving requests.
/// </summary>
public class DatabaseInitializationService : IHostedService
{
    private readonly DatabaseInitializer _initializer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseInitializationService> _logger;

    public DatabaseInitializationService(
        DatabaseInitializer initializer,
        IServiceScopeFactory scopeFactory,
        ILogger<DatabaseInitializationService> logger)
    {
        _initializer = initializer;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetService<MorpheoDbContext>();
        if (context is null)
        {
            _logger.LogDebug("No MorpheoDbContext registered — skipping database initialization.");
            return;
        }

        try
        {
            _logger.LogInformation("Initializing Morpheo database...");
            await _initializer.InitializeAsync(context);
            _logger.LogInformation("Database initialization completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to initialize database. Application cannot start.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
