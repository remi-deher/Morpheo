using Microsoft.AspNetCore.Http;
using Morpheo.Sdk;

namespace Morpheo.Core.Security;

/// <summary>
/// Default authentication strategy (Convention): Allow everything.
/// Ideal for development or isolated networks without configuration.
/// </summary>
public class AllowAllAuthenticator : IRequestAuthenticator
{
    public Task<bool> IsAuthorizedAsync(HttpContext context)
    {
        return Task.FromResult(true);
    }
}
