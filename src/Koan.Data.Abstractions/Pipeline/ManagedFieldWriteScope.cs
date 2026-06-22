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

    /// <summary>The managed-field values for the current write, or <c>null</c> when none is in scope.</summary>
    public static IReadOnlyDictionary<string, object?>? Current => _current.Value;

    /// <summary>Enter a write scope carrying <paramref name="values"/>; dispose restores the previous scope.</summary>
    public static IDisposable Enter(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var prev = _current.Value;
        _current.Value = values;
        return new Scope(prev);
    }

    private sealed class Scope(IReadOnlyDictionary<string, object?>? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = previous;
        }
    }
}
