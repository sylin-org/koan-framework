namespace Koan.Data.AI;

/// <summary>
/// Global registry of entity types with [Embedding] attribute.
/// Populated during startup by EmbeddingAutoRegistrar.
/// </summary>
internal static class EmbeddingRegistry
{
    private static readonly List<Type> _registeredTypes = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Registers entity types with [Embedding] attribute.
    /// Called once during startup by KoanAutoRegistrar.
    /// </summary>
    public static void RegisterTypes(IEnumerable<Type> types)
    {
        lock (_lock)
        {
            _registeredTypes.AddRange(types);
        }
    }

    /// <summary>
    /// Gets all registered entity types with [Embedding] attribute.
    /// </summary>
    public static IReadOnlyList<Type> GetRegisteredTypes()
    {
        lock (_lock)
        {
            return _registeredTypes.ToList();
        }
    }

    /// <summary>
    /// Gets entity types that use async embedding (for background worker).
    /// </summary>
    public static IEnumerable<Type> AsyncEntityTypes
    {
        get
        {
            lock (_lock)
            {
                return _registeredTypes
                    .Where(t => EmbeddingMetadata.Get(t).Async)
                    .ToList();
            }
        }
    }
}
