using Microsoft.Extensions.DependencyInjection;

namespace Morpheo.Core.Configuration;

/// <summary>
/// Interface for configuring Morpheo services.
/// </summary>
public interface IMorpheoBuilder
{
    /// <summary>
    /// Gets the service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
