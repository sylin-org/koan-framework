using System.Collections.Concurrent;

namespace Koan.Core.Hosting.Bootstrap;

/// <summary>
/// Registry to track already-invoked initializers and prevent duplicate module loading.
/// Follows Koan's principle of minimal scaffolding while providing essential guardrails.
/// </summary>
public sealed class InitializerRegistry
{
    private readonly ConcurrentHashSet<string> _invokedInitializers = new();
    private readonly ConcurrentHashSet<string> _invokedModules = new();
    
    /// <summary>
    /// Singleton instance for the current application domain.
    /// </summary>
    public static InitializerRegistry Instance { get; } = new();
    
    private InitializerRegistry() { }
    
    /// <summary>
    /// Attempts to register an initializer as invoked. Returns true if this is the first invocation.
    /// </summary>
    /// <param name="initializerType">The Type of the IKoanInitializer</param>
    /// <returns>True if this is the first invocation, false if already invoked</returns>
    public bool TryRegisterInitializer(Type initializerType)
    {
        var key = initializerType.FullName ?? initializerType.Name;
        return _invokedInitializers.Add(key);
    }
    
    /// <summary>
    /// Attempts to register a module as loaded. Returns true if this is the first loading.
    /// </summary>
    /// <param name="moduleName">The name/identifier of the module</param>
    /// <returns>True if this is the first loading, false if already loaded</returns>
    public bool TryRegisterModule(string moduleName)
    {
        return _invokedModules.Add(moduleName);
    }
    
    /// <summary>
    /// Checks if an initializer has already been invoked.
    /// </summary>
    public bool IsInitializerInvoked(Type initializerType)
    {
        var key = initializerType.FullName ?? initializerType.Name;
        return _invokedInitializers.Contains(key);
    }
    
    /// <summary>
    /// Checks if a module has already been loaded.
    /// </summary>
    public bool IsModuleLoaded(string moduleName)
    {
        return _invokedModules.Contains(moduleName);
    }
    
    /// <summary>
    /// Gets all invoked initializer types for diagnostics.
    /// </summary>
    public IReadOnlySet<string> GetInvokedInitializers() => _invokedInitializers.ToHashSet();
    
    /// <summary>
    /// Gets all loaded module names for diagnostics.
    /// </summary>
    public IReadOnlySet<string> GetLoadedModules() => _invokedModules.ToHashSet();
    
    /// <summary>
    /// Clears all registration tracking. Used primarily for testing.
    /// </summary>
    public void Clear()
    {
        _invokedInitializers.Clear();
        _invokedModules.Clear();
    }
}

/// <summary>
/// Thread-safe HashSet implementation for tracking invocations.
/// </summary>
internal sealed class ConcurrentHashSet<T> where T : notnull
{
    private readonly ConcurrentDictionary<T, byte> _dictionary = new();
    
    public bool Add(T item) => _dictionary.TryAdd(item, 0);
    
    public bool Contains(T item) => _dictionary.ContainsKey(item);
    
    public HashSet<T> ToHashSet() => new(_dictionary.Keys);
    
    public void Clear() => _dictionary.Clear();
}