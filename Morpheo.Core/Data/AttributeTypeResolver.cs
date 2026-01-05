using System.Reflection;
using Morpheo.Sdk;

namespace Morpheo.Core.Data;

/// <summary>
/// Resolves types based on the <see cref="MorpheoTypeAttribute"/> or class name.
/// </summary>
public class AttributeTypeResolver : IEntityTypeResolver
{
    private readonly Dictionary<string, Type> _nameToType = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, string> _typeToName = new();

    public AttributeTypeResolver()
    {
        ScanAssemblies();
    }

    private void ScanAssemblies()
    {
        // Scan all loaded assemblies for types with MorpheoTypeAttribute
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            try
            {
                var types = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && typeof(MorpheoEntity).IsAssignableFrom(t));

                foreach (var type in types)
                {
                    var attr = type.GetCustomAttribute<MorpheoTypeAttribute>();
                    var name = attr?.Name ?? type.Name;

                    if (!_nameToType.ContainsKey(name))
                    {
                        _nameToType[name] = type;
                        _typeToName[type] = name;
                    }
                }
            }
            catch
            {
                // Ignore scanning errors (e.g. dynamic assemblies)
            }
        }
    }

    public Type? ResolveType(string entityName)
    {
        _nameToType.TryGetValue(entityName, out var type);
        return type;
    }

    public string GetNetworkName(Type type)
    {
        if (_typeToName.TryGetValue(type, out var name))
            return name;
        
        // Fallback for types not scanned or added dynamically?
        // Check attribute manually
        var attr = type.GetCustomAttribute<MorpheoTypeAttribute>();
        return attr?.Name ?? type.Name;
    }
}
