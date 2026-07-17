using System.Collections.Concurrent;

namespace Koan.Data.AI;

/// <summary>
/// Process-wide registry of immutable entity-type discovery facts for types tagged with
/// <see cref="Attributes.MediaAnalysisAttribute"/>.
/// Populated by loaded-assembly discovery; entries are additive, idempotent, and independent of any
/// host, service provider, configuration, or backend.
/// </summary>
public static class MediaAnalysisRegistry
{
    private static readonly ConcurrentDictionary<Type, byte> _registeredTypes = new(TypeEqualityComparer.Instance);

    /// <summary>
    /// Infrastructure entry point for recording a batch of entity types with <c>[MediaAnalysis]</c>.
    /// Registration is process-lifetime discovery, not per-host activation or a supported runtime
    /// extension mechanism.
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
    /// Used by loaded-assembly discovery in <see cref="Initialization.DataAiModule"/>; this records
    /// a process-lifetime type fact and does not activate host-owned behavior.
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
