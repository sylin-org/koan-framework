using System.Collections.Concurrent;
using System.Linq;

namespace Koan.Data.AI;

/// <summary>
/// Global registry of entity types tagged with <see cref="Attributes.EmbeddingAttribute"/>.
/// Populated via source-generated module initializers; remains additive for runtime extensibility.
/// </summary>
public static partial class EmbeddingRegistry
{
    private static readonly ConcurrentDictionary<Type, byte> _registeredTypes = new(TypeEqualityComparer.Instance);

    /// <summary>
    /// Registers entity types with <c>[Embedding]</c> attribute.
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
    /// Gets all registered entity types with <c>[Embedding]</c> attribute.
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
    /// Gets entity types that opt into async embedding (used by background worker orchestration).
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
                .Where(t => EmbeddingMetadata.Resolve(t).Async)
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
