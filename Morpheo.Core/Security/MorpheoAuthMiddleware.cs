using Microsoft.AspNetCore.Http;

namespace Morpheo.Core.Security;

/// <summary>
/// Middleware checking Node Enrollment status for every request.
/// </summary>
public class MorpheoAuthMiddleware
{
    private readonly RequestDelegate _next;

    public MorpheoAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context, 
        IPeerTrustStore trustStore, 
        INodeEnrollmentStrategy enrollmentStrategy)
    {
        // 1. Identify Node
        var nodeId = context.Request.Headers["X-Morpheo-NodeId"].ToString();
        
        // Allow un-identified requests? Usually Morpheo internal calls or dashboard might not have NodeId.
        // If it's a dashboard request (e.g. browser), we might exclude it or rely on Cookie/Other auth.
        // For System API (/morpheo/*) we assume NodeId logic usually applies, but let's be careful.
        // Simplification: Only enforce for /morpheo/ paths?
        bool isSystemRequest = context.Request.Path.StartsWithSegments("/morpheo");

        if (!isSystemRequest)
        {
            // Dashboard or other assets -> proceed (or use other auth middleware)
            await _next(context);
            return;
        }
        
        if (string.IsNullOrEmpty(nodeId))
        {
            // No NodeId on a system request -> 401 Unauthorized? Or allow if it's just a probe?
            // Let's assume strictness for now.
            // But wait, SharedSecret strategy might need to run even if NodeId is missing? No, strategies check NodeId.
            // Assume NodeId is required.
            
            // Correction: Some discovery probes might not have it inside header?
            // Let's pass to strategy if not trusted.
            // But if NodeId is null, TrustStore check fails.
            context.Response.StatusCode = 401; 
            await context.Response.WriteAsync("Missing X-Morpheo-NodeId");
            return;
        }

        // 2. Check Trust Store
        if (trustStore.IsTrusted(nodeId))
        {
            // Known good peer
            await _next(context);
            return;
        }

        // 3. Evaluate Enrollment
        var result = await enrollmentStrategy.EvaluateAccessAsync(context);

        switch (result)
        {
            case EnrollmentResult.Allowed:
                await _next(context);
                break;

            case EnrollmentResult.Pending:
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("WAITING_APPROVAL");
                break;

            case EnrollmentResult.Denied:
            default:
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("ACCESS_DENIED");
                break;
        }
    }
}
