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
    /// of the returned scope. Restores in registration order and disposes in reverse. <b>Fail-closed:</b> a bag
    /// key with no registered carrier throws <see cref="AmbientCarrierException"/> (never a silent fail-open); if a
    /// carrier's own <c>Restore</c> throws, any already-acquired scopes are unwound before the exception
    /// propagates. A <c>null</c>/empty bag restores nothing and returns a safe no-op scope.
    /// </summary>
    public IDisposable Restore(IReadOnlyDictionary<string, string>? bag)
    {
        if (bag is null || bag.Count == 0) return NoopScope.Instance;

        List<IDisposable>? scopes = null;
        var consumed = 0;
        try
        {
            for (var i = 0; i < _carriers.Count; i++)
            {
                if (!bag.TryGetValue(_carriers[i].AxisKey, out var captured)) continue;
                (scopes ??= new List<IDisposable>()).Add(_carriers[i].Restore(captured));
                consumed++;
            }

            if (consumed != bag.Count)
            {
                var unregistered = bag.Keys.Where(k => !_byKey.ContainsKey(k)).ToArray();
                throw new AmbientCarrierException(unregistered);
            }
        }
        catch
        {
            Unwind(scopes);
            throw;
        }

        return scopes is null ? NoopScope.Instance : new CompositeScope(scopes);
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
