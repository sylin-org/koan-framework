using Koan.Canon.Domain.Metadata;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Base contract for pipeline context snapshots shared with observers.
/// </summary>
public interface ICanonPipelineContext
{
    /// <summary>
    /// Canonization options for the current operation.
    /// </summary>
    CanonizationOptions Options { get; }

    /// <summary>
    /// Metadata associated with the entity.
    /// </summary>
    CanonMetadata Metadata { get; }

    /// <summary>
    /// Type of the canonical entity being processed.
    /// </summary>
    Type EntityType { get; }

    /// <summary>
    /// Arbitrary context items.
    /// </summary>
    IReadOnlyDictionary<string, object?> Items { get; }

    /// <summary>
    /// Attempts to retrieve a context item by key.
    /// </summary>
    bool TryGetItem<TValue>(string key, out TValue? value);
}
