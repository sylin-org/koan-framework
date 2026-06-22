namespace Koan.Data.Core.Pipeline;

/// <summary>
/// The boot-time index of external <see cref="FieldTransformContributor"/>s (DATA-0105 §0 / ARCH-0098 §0). A
/// cross-cutting module registers its contributor from its module <c>Start</c> (where the built service provider is
/// available to capture the crypto dependencies); the facade reads them generically and never names the axis.
/// Mirrors the write-stamp / managed-field registries (static boot index, <see cref="IsEmpty"/> volatile off-gate
/// making the off path byte-identical, idempotent registration).
///
/// <para><b>Off = structurally absent:</b> when no module registers, <see cref="IsEmpty"/> is <c>true</c>, the
/// facade builds an empty transform plan and skips every clone/encrypt/reverse — byte-identical to pre-change. Each
/// registration invalidates the per-type plan memo.</para>
/// </summary>
public static class StorageFieldTransformRegistry
{
    private static readonly object _gate = new();
    private static readonly List<FieldTransformContributor> _contributors = new();
    private static volatile bool _isEmpty = true;

    /// <summary>Whether no field transform is registered — the hot-path off gate. Cheap volatile read.</summary>
    public static bool IsEmpty => _isEmpty;

    /// <summary>Every registered contributor, in registration order.</summary>
    public static IReadOnlyList<FieldTransformContributor> All
    {
        get { lock (_gate) return _contributors.ToArray(); }
    }

    /// <summary>
    /// Whether <paramref name="entityType"/> has any field transform — i.e. its stored value differs from the value
    /// returned on read. The public probe the cache layer uses to exclude such types from the distributed cache (a
    /// cached value would be post-reverse plaintext; ARCH-0098 §6). Type-plane memoized via the plan; cheap off-gate.
    /// </summary>
    public static bool HasTransformsFor(Type entityType)
        => !_isEmpty && StorageFieldTransformPlan.For(entityType).HasTransforms;

    /// <summary>
    /// Register a field-transform contributor. Boot-only. Idempotent by <see cref="FieldTransformContributor.Id"/>.
    /// Invalidates the per-type plan memo.
    /// </summary>
    public static void Register(FieldTransformContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        if (string.IsNullOrWhiteSpace(contributor.Id))
            throw new ArgumentException("A field-transform contributor must have a non-empty Id.", nameof(contributor));
        lock (_gate)
        {
            if (_contributors.Any(c => string.Equals(c.Id, contributor.Id, StringComparison.Ordinal)))
                return;
            _contributors.Add(contributor);
            _isEmpty = false;
        }
        StorageFieldTransformPlan.InvalidateCache();
    }

    /// <summary>Test-support: clear all registrations and the plan memo.</summary>
    public static void Reset()
    {
        lock (_gate)
        {
            _contributors.Clear();
            _isEmpty = true;
        }
        StorageFieldTransformPlan.InvalidateCache();
    }
}
