using Morpheo.Sdk;

namespace Morpheo.Core.Data;

/// <summary>
/// A simple implementation of <see cref="IEntityTypeResolver"/> using a manual registration dictionary.
/// </summary>
public class SimpleTypeResolver : IEntityTypeResolver
{
    private readonly Dictionary<string, Type> _mapping = new();

    /// <summary>
    /// Registers a type for resolution.
    /// </summary>
    /// <typeparam name="T">The type to register.</typeparam>
    public void Register<T>()
    {
        _mapping[typeof(T).Name] = typeof(T);
    }

    /// <inheritdoc/>
    public Type? ResolveType(string entityName)
    {
        _mapping.TryGetValue(entityName, out var type);
        return type;
    }

    /// <inheritdoc/>
    public string GetNetworkName(Type type)
    {
        return type.Name; // Fallback to class name
    }
}
