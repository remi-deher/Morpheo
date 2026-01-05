namespace Morpheo.Sdk;

/// <summary>
/// Contract for any entity capable of merging itself (CRDT).
/// The Framework will use this method in case of vector conflict.
/// </summary>
/// <typeparam name="T">The type of the entity (e.g., Product, Customer).</typeparam>
public interface IMergeable<T>
{
    /// <summary>
    /// Merges the current (local) instance with a remote version.
    /// Must return a new instance or modify the existing one to reflect the merged state.
    /// </summary>
    /// <param name="remote">The remote instance to merge with.</param>
    /// <returns>The merged instance.</returns>
    T Merge(T remote);
}

/// <summary>
/// Service allowing the resolution engine to retrieve the C# Type 
/// from the entity name stored in the database (e.g., "Product" -> typeof(Morpheo.App.Product)).
/// </summary>
public interface IEntityTypeResolver
{
    /// <summary>
    /// Resolves the C# Type from the entity name.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <returns>The resolved Type, or null if not found.</returns>
    Type? ResolveType(string entityName);
}
