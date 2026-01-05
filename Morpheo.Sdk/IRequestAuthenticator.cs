using Microsoft.AspNetCore.Http;
using System.Net.Http;

namespace Morpheo.Sdk;

/// <summary>
/// Defines the contract for validating incoming requests (Gatekeeper).
/// </summary>
public interface IRequestAuthenticator
{
    /// <summary>
    /// Verifies if the HTTP request is authorized.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>True if authorized, False otherwise.</returns>
    Task<bool> IsAuthorizedAsync(HttpContext context);
}
