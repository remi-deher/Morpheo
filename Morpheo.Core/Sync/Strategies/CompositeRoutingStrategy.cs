using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Sync.Strategies;

/// <summary>
/// A routing strategy that combines multiple sub-strategies (providers).
/// </summary>
public class CompositeRoutingStrategy : ISyncRoutingStrategy
{
    private readonly IEnumerable<ISyncStrategyProvider> _providers;
    private readonly ILogger<CompositeRoutingStrategy> _logger;

    public CompositeRoutingStrategy(
        IEnumerable<ISyncStrategyProvider> providers,
        ILogger<CompositeRoutingStrategy> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task PropagateAsync(
        SyncLogDto log,
        IEnumerable<PeerInfo> candidates,
        Func<PeerInfo, SyncLogDto, Task<bool>> sendFunc)
    {
        if (_providers == null || !_providers.Any()) return;

        // Launch all providers in parallel
        var tasks = _providers.Select(async provider =>
        {
            try
            {
                await provider.PropagateAsync(log, candidates, sendFunc);
            }
            catch (Exception ex)
            {
                // Log error but do not block other strategies
                _logger.LogError(ex, "Error executing strategy {StrategyType}", provider.GetType().Name);
            }
        });

        await Task.WhenAll(tasks);
    }
}
