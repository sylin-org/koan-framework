using System;
using System.Collections.Generic;
using System.Linq;

namespace Koan.Data.Core;

/// <summary>
/// ARCH-0100: the durable ambient carrier. Aggregates the DI-enumerable set of <see cref="IAmbientSliceCarrier"/>
/// and exposes the two verbs the durable transports (<c>Koan.Jobs</c>, later <c>Koan.Messaging</c>) call —
/// <see cref="Capture"/> at submit, <see cref="Restore"/> at execute. The transport names no axis; this registry
/// is the only thing that knows how to snapshot/rehydrate ambient slices, and the slices themselves stay owned by
/// their modules. The carrier set is fixed at construction (host build), so there is no submit-time race.
/// </summary>
public sealed class AmbientCarrierRegistry
{
    // Registration order is the deterministic restore order; reverse is the dispose order.
    private readonly IReadOnlyList<IAmbientSliceCarrier> _carriers;
    private readonly Dictionary<string, IAmbientSliceCarrier> _byKey;

    public AmbientCarrierRegistry(IEnumerable<IAmbientSliceCarrier> carriers)
    {
        _carriers = carriers?.ToArray() ?? throw new ArgumentNullException(nameof(carriers));
        _byKey = new Dictionary<string, IAmbientSliceCarrier>(_carriers.Count, StringComparer.Ordinal);
        foreach (var c in _carriers)
        {
            if (!_byKey.TryAdd(c.AxisKey, c))
                throw new InvalidOperationException(
                    $"Duplicate ambient carrier axis key '{c.AxisKey}': two carriers claim the same axis.");
        }
    }

    /// <summary>
    /// Snapshot every carriable ambient slice currently in scope into a portable bag keyed by axis. Returns
    /// <c>null</c> (not an empty map) when no carrier yields a value — the hot-path common case allocates nothing
    /// and persists no field on the durable record.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Capture()
    {
        Dictionary<string, string>? bag = null;
        // Iterate registration order for a deterministic bag; only carriers with a value contribute.
        for (var i = 0; i < _carriers.Count; i++)
        {
            var v = _carriers[i].Capture();
            if (v is null) continue;
            (bag ??= new Dictionary<string, string>(StringComparer.Ordinal))[_carriers[i].AxisKey] = v;
        }
        return bag;
    }

    /// <summary>
    /// Rehydrate the slices described by <paramref name="bag"/> onto <see cref="EntityContext"/> for the lifetime
    /// of the returned scope. Establishes an <b>explicit</b> ambient for <i>every</i> registered axis — each axis
    /// is either <see cref="IAmbientSliceCarrier.Restore"/>d from the bag or <see cref="IAmbientSliceCarrier.Suppress"/>ed
    /// (explicitly cleared) — so the work never inherits the carrier thread's ambient (the inline-drain leak). Applies
    /// in registration order and disposes in reverse. <b>Fail-closed:</b> a bag key with no registered carrier throws
    /// <see cref="AmbientCarrierException"/> <i>before</i> anything is pushed (never a silent fail-open); if a carrier's
    /// own restore/suppress throws, any already-acquired scopes are unwound before the exception propagates. With no
    /// registered carriers the call is a true no-op (the hot path for an app without a cross-cutting module).
    /// </summary>
    public IDisposable Restore(IReadOnlyDictionary<string, string>? bag)
    {
        // Fail closed up front: a captured axis with no registered carrier here cannot be rehydrated.
        if (bag is not null && bag.Count > 0)
        {
            var unregistered = bag.Keys.Where(k => !_byKey.ContainsKey(k)).ToArray();
            if (unregistered.Length > 0) throw new AmbientCarrierException(unregistered);
        }

        if (_carriers.Count == 0) return NoopScope.Instance;   // nothing to restore OR suppress — no allocation

        var scopes = new List<IDisposable>(_carriers.Count);
        try
        {
            for (var i = 0; i < _carriers.Count; i++)
            {
                var carrier = _carriers[i];
                scopes.Add(bag is not null && bag.TryGetValue(carrier.AxisKey, out var captured)
                    ? carrier.Restore(captured)     // captured value for this axis
                    : carrier.Suppress());          // no value → clear, don't inherit the carrier thread's ambient
            }
        }
        catch
        {
            Unwind(scopes);
            throw;
        }

        return new CompositeScope(scopes);
    }

    private static void Unwind(List<IDisposable>? scopes)
    {
        if (scopes is null) return;
        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            try { scopes[i].Dispose(); } catch { /* best-effort unwind; never mask the original failure */ }
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }

    private sealed class CompositeScope : IDisposable
    {
        private readonly List<IDisposable> _scopes;
        private bool _disposed;
        public CompositeScope(List<IDisposable> scopes) => _scopes = scopes;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unwind(_scopes);   // reverse order
        }
    }
}
