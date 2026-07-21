using System.Collections.Concurrent;
using System.Linq;

namespace Koan.Data.AI;

/// <summary>
/// Process-wide registry of immutable entity-type discovery facts for types tagged with
/// <see cref="Attributes.EmbeddingAttribute"/>.
/// Populated by source-generated module initializers; entries are additive, idempotent, and
/// independent of any host, service provider, configuration, or backend.
/// </summary>
public static partial class EmbeddingRegistry
{
    private static readonly ConcurrentDictionary<Type, byte> _registeredTypes = new(TypeEqualityComparer.Instance);

    /// <summary>
    /// Infrastructure entry point used by generated module initializers to record entity types with
    /// <c>[Embedding]</c>. Registration is process-lifetime discovery, not per-host activation or a
    /// supported runtime extension mechanism.
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
