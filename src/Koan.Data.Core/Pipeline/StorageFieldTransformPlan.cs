using System.Collections.Concurrent;

namespace Koan.Data.Core.Pipeline;

/// <summary>
/// The per-type set of <see cref="IFieldTransform"/>s for an entity (ARCH-0098 §0) — composed once per type from
/// the registered contributors and memoized at the Type plane. The facade calls <see cref="CloneForWrite"/> before
/// persist (clone + protect, so the caller keeps plaintext) and <see cref="ApplyOnRead"/> on every entity returned
/// from a read (restore plaintext in place). When the registry is empty the plan is empty and the facade skips it
/// entirely — byte-identical to pre-change.
/// </summary>
internal sealed class StorageFieldTransformPlan
{
    private static readonly ConcurrentDictionary<Type, StorageFieldTransformPlan> Cache = new();

    private readonly IFieldTransform[] _transforms;

    private StorageFieldTransformPlan(IFieldTransform[] transforms) => _transforms = transforms;

    /// <summary>Whether this type has any field transform — the hot-path short-circuit.</summary>
    public bool HasTransforms => _transforms.Length > 0;

    public static StorageFieldTransformPlan For(Type entityType) => Cache.GetOrAdd(entityType, static t => Build(t));

    /// <summary>Drop the per-type plan memo. Called when a contributor registration changes (boot-only ⇒ rare).</summary>
    internal static void InvalidateCache() => Cache.Clear();

    private static StorageFieldTransformPlan Build(Type entityType)
    {
        if (StorageFieldTransformRegistry.IsEmpty)
            return new StorageFieldTransformPlan(Array.Empty<IFieldTransform>());

        var list = new List<IFieldTransform>();
        foreach (var contributor in StorageFieldTransformRegistry.All)
        {
            var transform = contributor.Build(entityType);
            if (transform is not null) list.Add(transform);
        }
        return new StorageFieldTransformPlan(list.ToArray());
    }

    /// <summary>
    /// Clone <paramref name="entity"/> and apply every write transform to the clone (encrypt the protected fields),
    /// returning the clone to persist. The caller's <paramref name="entity"/> is never mutated.
    /// </summary>
    public object CloneForWrite(object entity)
    {
        var clone = EntityCloner.ShallowClone(entity);
        foreach (var t in _transforms) t.ApplyOnWrite(clone);
        return clone;
    }

    /// <summary>Apply every read transform to <paramref name="entity"/> in place (restore plaintext).</summary>
    public void ApplyOnRead(object entity)
    {
        foreach (var t in _transforms) t.ApplyOnRead(entity);
    }
}
