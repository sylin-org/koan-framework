namespace Koan.Data.Abstractions.Pipeline;

/// <summary>
/// The per-operation snapshot of managed-field values for the <b>current write</b> (DATA-0105 §3b, Seam 2).
/// The chokepoint (<c>RepositoryFacade</c>) computes the values once — reading ambient ONCE — and
/// <see cref="Enter"/>s this scope immediately before the inner write; the relational Serialize-stage contract
/// resolver reads <see cref="Current"/> (never live ambient) to inject the managed keys into the persisted
/// record. The scope is restored on dispose (set-around-inner-call).
///
/// <para>AsyncLocal is copy-on-write per async context and Newtonsoft serialization is synchronous between
/// <see cref="Enter"/> and the inner write, so concurrent operations never bleed snapshots.</para>
/// </summary>
public static class ManagedFieldWriteScope
{
    private static readonly AsyncLocal<IReadOnlyDictionary<string, object?>?> _current = new();
    private static readonly AsyncLocal<IReadOnlyDictionary<string, object?>?> _overrides = new();

    /// <summary>
    /// The <b>guarded</b> isolation values for the current write (tenant / classification), or <c>null</c> when none
    /// is in scope. These are both injected AND enforced by the adapter's conflict-aware upsert (no cross-scope
    /// takeover). The guard reads exactly this set.
    /// </summary>
    public static IReadOnlyDictionary<string, object?>? Current => _current.Value;

    /// <summary>
    /// The <b>unguarded</b> operation-override values for the current write (ARCH-0101 §4) — a mutable <i>state</i>
    /// field an operation rewrites, e.g. soft-delete's <c>__deleted = true</c>. Injected into the persisted record but
    /// NOT added to the conflict guard (the field is changing by design, so guarding it would reject the very write
    /// that sets it). <c>null</c> when no override is in scope.
    /// </summary>
    public static IReadOnlyDictionary<string, object?>? Overrides => _overrides.Value;

    /// <summary>
    /// The values an injection site must persist: the guarded isolation values merged with the unguarded operation
    /// overrides. Adapters/serializers INJECT from this; the conflict guard reads only <see cref="Current"/>.
    /// <b>Isolation (<see cref="Current"/>) WINS on a key collision</b> — an unguarded override can never clobber a
    /// guarded isolation field (e.g. a mis-declared <c>OnDelete</c> on <c>__koan_tenant</c> cannot re-stamp the tenant
    /// through the unguarded channel); overrides only fill non-isolation keys. <c>null</c> when neither is in scope
    /// (the byte-identical fast path).
    /// </summary>
    public static IReadOnlyDictionary<string, object?>? Effective
    {
        get
        {
            var cur = _current.Value;
            var ov = _overrides.Value;
            if (ov is null || ov.Count == 0) return cur;
            if (cur is null || cur.Count == 0) return ov;
            var merged = new Dictionary<string, object?>(cur, StringComparer.Ordinal);
            foreach (var kv in ov) if (!merged.ContainsKey(kv.Key)) merged[kv.Key] = kv.Value;   // isolation wins
            return merged;
        }
    }

    /// <summary>
    /// Enter a write scope. <paramref name="values"/> are the guarded isolation values; <paramref name="overrides"/>
    /// (optional) are the unguarded operation-override state values. Dispose restores the previous scope.
    /// </summary>
    public static IDisposable Enter(IReadOnlyDictionary<string, object?> values, IReadOnlyDictionary<string, object?>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        var prevCurrent = _current.Value;
        var prevOverrides = _overrides.Value;
        _current.Value = values;
        _overrides.Value = overrides;
        return new Scope(prevCurrent, prevOverrides);
    }

    /// <summary>
    /// Enter a write scope carrying ONLY unguarded operation-override values (no isolation values) — the soft-delete
    /// rewrite path when the entity is not also isolation-scoped. Dispose restores the previous scope.
    /// </summary>
    public static IDisposable EnterOverrides(IReadOnlyDictionary<string, object?> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var prevCurrent = _current.Value;
        var prevOverrides = _overrides.Value;
        _overrides.Value = overrides;
        return new Scope(prevCurrent, prevOverrides);
    }

    private sealed class Scope(IReadOnlyDictionary<string, object?>? prevCurrent, IReadOnlyDictionary<string, object?>? prevOverrides) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = prevCurrent;
            _overrides.Value = prevOverrides;
        }
    }
}
