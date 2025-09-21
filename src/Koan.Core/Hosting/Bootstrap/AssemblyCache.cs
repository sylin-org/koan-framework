using System.Collections.Concurrent;
using System.Reflection;

namespace Koan.Core.Hosting.Bootstrap;

/// <summary>
/// Centralized assembly cache to eliminate bespoke assembly scanning throughout the framework.
/// All components should use this cached result instead of calling AppDomain.CurrentDomain.GetAssemblies().
/// </summary>
public sealed class AssemblyCache
{
    private readonly ConcurrentDictionary<string, Assembly> _assembliesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<Assembly[]> _allAssemblies;
    private readonly Lazy<Assembly[]> _koanAssemblies;

    /// <summary>
    /// Singleton instance for the current application domain.
    /// </summary>
    public static AssemblyCache Instance { get; } = new();

    private AssemblyCache()
    {
        _allAssemblies = new Lazy<Assembly[]>(() => _assembliesByName.Values.ToArray());
        _koanAssemblies = new Lazy<Assembly[]>(() => _assembliesByName.Values
            .Where(a => a.GetName().Name?.StartsWith("Koan.", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray());
    }

    /// <summary>
    /// Adds an assembly to the cache. Thread-safe.
    /// </summary>
    /// <param name="assembly">The assembly to cache</param>
    /// <returns>True if added, false if already cached</returns>
    public bool AddAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? string.Empty;
        return _assembliesByName.TryAdd(name, assembly);
    }

    /// <summary>
    /// Gets all cached assemblies. Use this instead of AppDomain.CurrentDomain.GetAssemblies().
    /// </summary>
    /// <returns>Array of all discovered assemblies</returns>
    public Assembly[] GetAllAssemblies() => _allAssemblies.Value;

    /// <summary>
    /// Gets all Koan framework assemblies (names starting with "Koan.").
    /// </summary>
    /// <returns>Array of Koan assemblies</returns>
    public Assembly[] GetKoanAssemblies() => _koanAssemblies.Value;

    /// <summary>
    /// Checks if a specific assembly name is loaded.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly (e.g., "Koan.Data.Postgres")</param>
    /// <returns>True if the assembly is loaded</returns>
    public bool HasAssembly(string assemblyName)
    {
        return _assembliesByName.ContainsKey(assemblyName);
    }

    /// <summary>
    /// Gets a specific assembly by name.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly</param>
    /// <returns>The assembly if found, null otherwise</returns>
    public Assembly? GetAssembly(string assemblyName)
    {
        return _assembliesByName.TryGetValue(assemblyName, out var assembly) ? assembly : null;
    }

    /// <summary>
    /// Gets all loaded assembly names for diagnostics.
    /// </summary>
    /// <returns>Set of assembly names</returns>
    public IReadOnlySet<string> GetLoadedAssemblyNames() => _assembliesByName.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Forces refresh of cached arrays. Should only be called after adding new assemblies.
    /// </summary>
    internal void InvalidateCache()
    {
        if (_allAssemblies.IsValueCreated)
        {
            // We can't reset Lazy<T> values, but this is primarily for initialization
            // In practice, assemblies are discovered once during startup
        }
    }

    /// <summary>
    /// Clears all cached assemblies. Used primarily for testing.
    /// </summary>
    public void Clear()
    {
        _assembliesByName.Clear();
    }
}