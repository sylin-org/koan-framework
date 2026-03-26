using System.Collections.Concurrent;

namespace Koan.Data.AI;

/// <summary>
/// Global registry of entity types tagged with <see cref="Attributes.MediaAnalysisAttribute"/>.
/// Populated via source-generated module initializers; remains additive for runtime extensibility.
/// Mirrors <see cref="EmbeddingRegistry"/> for media analysis entities.
/// </summary>
public static class MediaAnalysisRegistry
{
    private static readonly ConcurrentDictionary<Type, byte> _registeredTypes = new(TypeEqualityComparer.Instance);

    /// <summary>
    /// Registers entity types with <c>[MediaAnalysis]</c> attribute.
    /// Source generators call this during module initialization; runtime callers can extend as needed.
    /// </summary>
    public static void RegisterTypes(IEnumerable<Type> types)
    {
        if (types is null) return;

        foreach (var type in types)
        {
            if (type is null) continue;
            _registeredTypes.TryAdd(type, 0);
        }
    }

    /// <summary>
    /// Registers a single entity type with <c>[MediaAnalysis]</c> attribute.
    /// Used by assembly scanning in <see cref="Initialization.KoanAutoRegistrar"/>.
    /// </summary>
    public static void Register(Type type, bool async)
    {
        if (type is null) return;
        _registeredTypes.TryAdd(type, 0);
    }

    /// <summary>
    /// Gets all registered entity types with <c>[MediaAnalysis]</c> attribute.
    /// </summary>
    public static IReadOnlyList<Type> GetRegisteredTypes()
    {
        if (_registeredTypes.IsEmpty)
        {
            return [];
        }

        return _registeredTypes.Keys.ToArray();
    }

    /// <summary>
    /// Gets entity types that opt into async media analysis (used by background worker orchestration).
    /// </summary>
    public static IEnumerable<Type> AsyncEntityTypes
    {
        get
        {
            if (_registeredTypes.IsEmpty)
            {
                return [];
            }

            var snapshot = _registeredTypes.Keys.ToArray();
            return snapshot
                .Where(t => MediaAnalysisMetadata.Resolve(t)?.Async == true)
                .ToArray();
        }
    }

    internal static void ResetForTesting() => _registeredTypes.Clear();

    private sealed class TypeEqualityComparer : IEqualityComparer<Type>
    {
        public static TypeEqualityComparer Instance { get; } = new();
        public bool Equals(Type? x, Type? y) => x == y;
        public int GetHashCode(Type obj) => obj.GetHashCode();
    }
}
