using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morpheo.Core.Configuration;
using Morpheo.Sdk;

namespace Morpheo.Core.Extensions;

/// <summary>
/// Extension methods for configuring the Morpheo dashboard.
/// </summary>
public static class MorpheoDashboardExtensions
{
    /// <summary>
    /// Adds dashboard configuration to the DI container.
    /// </summary>
    /// <param name="builder">The Morpheo builder.</param>
    /// <param name="configure">Optional delegate to configure dashboard options.</param>
    /// <returns>The Morpheo builder.</returns>
    public static IMorpheoBuilder AddDashboard(this IMorpheoBuilder builder, Action<DashboardOptions>? configure = null)
    {
        var options = new DashboardOptions();
        configure?.Invoke(options);

        // Path validation
        if (!options.Path.StartsWith("/"))
        {
            options.Path = "/" + options.Path;
        }

        builder.Services.TryAddSingleton(options);

        return builder;
    }
}
