using Microsoft.AspNetCore.Http;

namespace Morpheo.Core.Security;

public enum EnrollmentResult
{
    Allowed,
    Denied,
    Pending
}

/// <summary>
/// Strategy to decide if a new peer can join the mesh.
/// </summary>
public interface INodeEnrollmentStrategy
{
    /// <summary>
    /// Evaluates if the incoming request should be allowed.
    /// </summary>
    Task<EnrollmentResult> EvaluateAccessAsync(HttpContext context);
}
