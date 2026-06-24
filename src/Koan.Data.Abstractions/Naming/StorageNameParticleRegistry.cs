using Koan.Core.Naming;

namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// The boot-time index of <see cref="IStorageNameParticleContributor"/>s (ARCH-0101 §3 — the container-name particle
/// plane). A cross-cutting module registers its contributor from its <c>KoanAutoRegistrar</c> (Reference = Intent);
/// <see cref="StorageNameGenerator"/> reads them generically and never names the axis.
///
/// <para>Deliberate static index (not DI) — the <b>same declared deviation as <c>ManagedFieldRegistry</c></b>
/// (DATA-0105 §4): the storage-name composer is static, cached, and reached deep in adapter naming (data and vector)
/// where no DI scope exists. <b>Off = structurally absent:</b> when no module registers, <see cref="IsEmpty"/> is
/// <c>true</c> and <see cref="Gather"/> returns an empty set, so every consumer short-circuits to its byte-identical
/// pre-change name.</para>
/// </summary>
public static class StorageNameParticleRegistry
{
    private static readonly object _gate = new();
    // Lock-free snapshot read on the hot path (Gather runs per name resolve / per data op); rebuilt under the gate on
    // the boot-only Register/Reset. Mirrors ManagedFieldRegistry's lock-free ForType memo (the cited prior art).
    private static volatile IStorageNameParticleContributor[] _snapshot = Array.Empty<IStorageNameParticleContributor>();

    /// <summary>Whether no contributor is registered — the hot-path off gate. Cheap volatile read.</summary>
    public static bool IsEmpty => _snapshot.Length == 0;

    /// <summary>
    /// Register a contributor. Boot-only. Idempotent by logical <see cref="IStorageNameParticleContributor.Axis"/>
    /// (mirrors <c>ManagedFieldRegistry</c>'s StorageName key) so a re-entrant Reference = Intent registrar is safe and
    /// two distinct axes never collide on CLR type.
    /// </summary>
    public static void Register(IStorageNameParticleContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        lock (_gate)
        {
            foreach (var c in _snapshot)
                if (string.Equals(c.Axis, contributor.Axis, StringComparison.Ordinal)) return;
            var next = new IStorageNameParticleContributor[_snapshot.Length + 1];
            Array.Copy(_snapshot, next, _snapshot.Length);
            next[^1] = contributor;
            _snapshot = next;
        }
    }

    /// <summary>
    /// The ambient name particles applicable to <paramref name="entityType"/> right now, in a total, stable order
    /// (by <see cref="Particle.Order"/>, ties broken by axis ordinal). Empty when nothing is registered or no
    /// contributor yields a particle in the current ambient (off / host) — the byte-identical fast path. Reads ambient
    /// per call: the result varies per axis value, so the caller MUST fold it into the name cache key.
    /// </summary>
    public static Particle[] Gather(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        var snapshot = _snapshot;            // single volatile read, lock-free
        if (snapshot.Length == 0) return Array.Empty<Particle>();
        List<Particle>? list = null;
        foreach (var c in snapshot)
        {
            if (c.GetParticle(entityType) is { } particle) (list ??= new()).Add(particle);
        }
        if (list is null) return Array.Empty<Particle>();
        if (list.Count > 1)
            list.Sort(static (a, b) =>
            {
                var o = a.Order.CompareTo(b.Order);
                return o != 0 ? o : string.CompareOrdinal(a.Axis, b.Axis);
            });
        return list.ToArray();
    }

    /// <summary>Test-support: clear all registrations (mirrors <c>ManagedFieldRegistry.Reset</c>).</summary>
    public static void Reset()
    {
        lock (_gate) _snapshot = Array.Empty<IStorageNameParticleContributor>();
    }
}
