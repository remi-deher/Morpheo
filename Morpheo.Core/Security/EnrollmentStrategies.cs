using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;

namespace Morpheo.Core.Security;

/// <summary>
/// Default strategy: Accepts everyone.
/// </summary>
public class AutoAcceptEnrollmentStrategy : INodeEnrollmentStrategy
{
    private readonly IPeerTrustStore _trustStore;

    public AutoAcceptEnrollmentStrategy(IPeerTrustStore trustStore)
    {
        _trustStore = trustStore;
    }

    public async Task<EnrollmentResult> EvaluateAccessAsync(HttpContext context)
    {
        var nodeId = context.Request.Headers["X-Morpheo-NodeId"].ToString();
        if (!string.IsNullOrEmpty(nodeId))
        {
            // Auto-trust
            await _trustStore.TrustPeerAsync(nodeId);
        }
        return EnrollmentResult.Allowed;
    }
}

/// <summary>
/// Strategy requiring manual approval via API.
/// </summary>
public class ManualApprovalEnrollmentStrategy : INodeEnrollmentStrategy
{
    private readonly IPeerTrustStore _trustStore;
    private readonly ConcurrentDictionary<string, DateTime> _pendingPeers = new();

    public ManualApprovalEnrollmentStrategy(IPeerTrustStore trustStore)
    {
        _trustStore = trustStore;
    }

    public Task<EnrollmentResult> EvaluateAccessAsync(HttpContext context)
    {
        var nodeId = context.Request.Headers["X-Morpheo-NodeId"].ToString();
        if (string.IsNullOrEmpty(nodeId)) return Task.FromResult(EnrollmentResult.Denied);

        // Add to pending list if not known
        _pendingPeers.TryAdd(nodeId, DateTime.UtcNow);

        return Task.FromResult(EnrollmentResult.Pending);
    }

    public IReadOnlyDictionary<string, DateTime> GetPendingPeers() => _pendingPeers;

    public async Task ApproveAsync(string nodeId)
    {
        if (_pendingPeers.TryRemove(nodeId, out _))
        {
            await _trustStore.TrustPeerAsync(nodeId);
        }
    }
}

/// <summary>
/// Strategy based on a shared secret (Header X-Morpheo-Secret).
/// Reads the secret dynamically from the configuration manager.
/// </summary>
public class SharedSecretEnrollmentStrategy : INodeEnrollmentStrategy
{
    private readonly IPeerTrustStore _trustStore;
    private readonly Morpheo.Core.Configuration.MorpheoConfigManager _configManager;

    public SharedSecretEnrollmentStrategy(IPeerTrustStore trustStore, Morpheo.Core.Configuration.MorpheoConfigManager configManager)
    {
        _trustStore = trustStore;
        _configManager = configManager;
    }

    public async Task<EnrollmentResult> EvaluateAccessAsync(HttpContext context)
    {
        var nodeId = context.Request.Headers["X-Morpheo-NodeId"].ToString();
        var incomingSecret = context.Request.Headers["X-Morpheo-Secret"].ToString();

        if (string.IsNullOrEmpty(nodeId)) return EnrollmentResult.Denied;

        // Dynamic Read
        var currentSecret = _configManager.Load().Security.EnrollmentSecret;

        if (string.IsNullOrEmpty(currentSecret)) 
        {
             // If no secret configured, fail safe -> Deny
             return EnrollmentResult.Denied;
        }

        if (incomingSecret == currentSecret)
        {
            await _trustStore.TrustPeerAsync(nodeId);
            return EnrollmentResult.Allowed;
        }

        return EnrollmentResult.Denied;
    }
}
