using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Morpheo.Core.Configuration;
using Morpheo.Core.Sync.Strategies;
using Morpheo.Sdk;

namespace Morpheo.Core.Extensions;

/// <summary>
/// Extension methods for cloud connectivity.
/// </summary>
public static class MorpheoCloudExtensions
{
    /// <summary>
    /// Adds a Central Server Relay strategy using HTTP Push.
    /// </summary>
    /// <param name="builder">The Morpheo builder.</param>
    /// <param name="serverUrl">The URL of the central server.</param>
    /// <returns>The Morpheo builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown if serverUrl is null or empty.</exception>
    /// <exception cref="ArgumentException">Thrown if serverUrl is invalid.</exception>
    public static IMorpheoBuilder AddCentralServerRelay(this IMorpheoBuilder builder, string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new ArgumentNullException(nameof(serverUrl));
        }

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Invalid central server URL.", nameof(serverUrl));
        }

        // Register HTTP Push strategy
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISyncStrategyProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<HttpPushStrategy>>();
            return new HttpPushStrategy(httpClientFactory, uri, logger);
        }));

        return builder;
    }
}
