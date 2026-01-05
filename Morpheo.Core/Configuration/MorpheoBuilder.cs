using Microsoft.Extensions.DependencyInjection;

namespace Morpheo.Core.Configuration;

/// <summary>
/// Default implementation of the Morpheo configuration builder.
/// </summary>
public class MorpheoBuilder : IMorpheoBuilder
{
    /// <inheritdoc/>
    public IServiceCollection Services { get; }

    public MorpheoBuilder(IServiceCollection services)
    {
        Services = services;
    }
}
