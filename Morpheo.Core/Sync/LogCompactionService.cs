using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;
using Morpheo.Core.Data;

namespace Morpheo.Core.Sync;

/// <summary>
/// Background service responsible for compacting (Garbage Collecting) old sync logs.
/// </summary>
public class LogCompactionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MorpheoOptions _options;
    private readonly ILogger<LogCompactionService> _logger;

    public LogCompactionService(IServiceProvider serviceProvider, MorpheoOptions options, ILogger<LogCompactionService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Compaction Service (GC) started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCompactionAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during log compaction.");
            }

            // Wait before next cycle
            await Task.Delay(_options.CompactionInterval, stoppingToken);
        }
    }

    private async Task RunCompactionAsync()
    {
        // Calculate threshold date
        var thresholdTick = DateTime.UtcNow.Subtract(_options.LogRetention).Ticks;

        // Delete obsolete logs via Store
        var store = _serviceProvider.GetRequiredService<ISyncLogStore>();
        var logsDeleted = await store.DeleteOldLogsAsync(thresholdTick);

        if (logsDeleted > 0)
        {
            _logger.LogInformation($"Compaction: {logsDeleted} old logs deleted.");
        }
    }
}
