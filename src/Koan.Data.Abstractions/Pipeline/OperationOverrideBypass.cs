namespace Koan.Data.Abstractions.Pipeline;

/// <summary>
/// The generic, <b>plane-specific AND target-scoped</b> bypass for the operation-semantics override plane
/// (ARCH-0101 §4). When active for a specific <c>(entityType, id)</c>, the facade performs that one delete's
/// <i>physical</i> default instead of the declared override (e.g. soft-delete) — but ALL other isolation is RETAINED
/// (the physical delete is still read-scoped, so a hard-delete can only remove a row the caller can see).
///
/// <para><b>Bounded by construction (the ADR §4 invariant):</b> the bypass is keyed to the exact <c>(type, id)</c> the
/// escape verb targeted, so it does NOT leak into a cascade / lifecycle handler that deletes a DIFFERENT entity during
/// the same async flow — that nested delete sees no matching bypass and takes the normal (soft) path. A type-agnostic
/// "bypass everything" flag would be a delete hole; this is not that.</para>
/// </summary>
public static class OperationOverrideBypass
{
    private static readonly AsyncLocal<(Type Type, object Id)?> _target = new();

    /// <summary>Whether the operation-override is bypassed for this exact <paramref name="entityType"/> + <paramref name="id"/>.</summary>
    public static bool IsBypassedFor(Type entityType, object id)
    {
        var t = _target.Value;
        return t is { } x && x.Type == entityType && Equals(x.Id, id);
    }

    /// <summary>Enter a bypass scope targeting exactly <paramref name="entityType"/> + <paramref name="id"/>; dispose restores.</summary>
    public static IDisposable Enter(Type entityType, object id)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(id);
        var prev = _target.Value;
        _target.Value = (entityType, id);
        return new Scope(prev);
    }

    private sealed class Scope(( Type Type, object Id)? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _target.Value = previous;
        }
    }
}
