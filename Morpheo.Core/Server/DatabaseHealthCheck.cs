using Microsoft.Extensions.Diagnostics.HealthChecks;
using Morpheo.Core.Sync;

namespace Morpheo.Core.Server;

/// <summary>
/// Readiness health check that reports Healthy only when the node's database is reachable.
/// Backs the <c>/health/ready</c> endpoint consumed by Docker, systemd and Kubernetes probes.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly DataSyncService _syncService;

    public DatabaseHealthCheck(DataSyncService syncService)
    {
        _syncService = syncService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var reachable = await _syncService.CanReachDatabaseAsync();
        return reachable
            ? HealthCheckResult.Healthy("Database reachable.")
            : HealthCheckResult.Unhealthy("Database unreachable.");
    }
}
