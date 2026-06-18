using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morpheo.Core.Configuration;
using Morpheo.Core.Security;
using Morpheo.Sdk;

namespace Morpheo.Core.Extensions;

/// <summary>
/// Extension methods for configuring inter-node security.
/// </summary>
public static class MorpheoSecurityExtensions
{
    /// <summary>
    /// Enables HMAC-SHA256 request authentication across the cluster using a shared secret (PSK).
    ///
    /// <para>
    /// Outgoing requests are signed by the client (<see cref="HmacSigningHandler"/>) and incoming
    /// requests are verified by the server (<see cref="PskHmacAuthenticator"/>). With
    /// <paramref name="requireFreshness"/> enabled, every request must additionally carry a
    /// timestamp and a single-use nonce, defeating replay attacks.
    /// </para>
    /// </summary>
    /// <param name="builder">The Morpheo builder.</param>
    /// <param name="preSharedKey">The cluster shared secret. Must be identical on every node.</param>
    /// <param name="requireFreshness">
    /// When true, requests without a valid timestamp + nonce are rejected (recommended).
    /// </param>
    /// <param name="clockSkew">Maximum accepted clock difference between nodes. Default 5 minutes.</param>
    /// <returns>The Morpheo builder.</returns>
    public static IMorpheoBuilder AddClusterSecurity(
        this IMorpheoBuilder builder,
        string preSharedKey,
        bool requireFreshness = true,
        TimeSpan? clockSkew = null)
    {
        if (string.IsNullOrWhiteSpace(preSharedKey))
            throw new ArgumentException("A cluster pre-shared key is required.", nameof(preSharedKey));

        // Store the secret on the options instance so the client-side signing handler can use it.
        var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(MorpheoOptions));
        if (descriptor?.ImplementationInstance is MorpheoOptions options)
        {
            options.ClusterSecret = preSharedKey;
        }

        // Replace the permissive default authenticator with the PSK gatekeeper.
        builder.Services.Replace(ServiceDescriptor.Singleton<IRequestAuthenticator>(
            _ => new PskHmacAuthenticator(preSharedKey, clockSkew, requireFreshness)));

        return builder;
    }
}
