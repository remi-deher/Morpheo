using Morpheo.Sdk;

namespace Morpheo.Sdk;

/// <summary>
/// Marker interface for synchronization strategy providers.
/// Enables multiple injection via CompositeRoutingStrategy.
/// </summary>
public interface ISyncStrategyProvider : ISyncRoutingStrategy
{
}
