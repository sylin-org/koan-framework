namespace Koan.Data.Core.Pipeline;

/// <summary>
/// The boot-time index of external <see cref="WriteStampContributor"/>s (DATA-0105 §0 / ARCH-0098 §0) — the "open
/// slot" on the write-stamp stage. A cross-cutting module registers its contributor from its
/// <c>KoanModule.Register</c> (Reference = Intent); <see cref="StorageWritePlan.Build"/> reads them
/// generically and never names the axis. Mirrors <c>ManagedFieldRegistry</c>'s mechanics (static boot index,
/// <see cref="IsEmpty"/> volatile off-gate making the off path byte-identical, idempotent registration).
///
/// <para><b>Off = structurally absent:</b> when no module registers, <see cref="IsEmpty"/> is <c>true</c> and the
/// plan composes exactly the built-in identity + <c>[Timestamp]</c> stamps. Registration is boot-only (registrars
/// run before any data op); each registration invalidates the per-type plan memo.</para>
/// </summary>
public static class StorageWriteContributorRegistry
{
    private static readonly object _gate = new();
    private static readonly List<WriteStampContributor> _contributors = new();
    private static volatile bool _isEmpty = true;

    /// <summary>Whether no external write contributor is registered — the hot-path off gate. Cheap volatile read.</summary>
    public static bool IsEmpty => _isEmpty;

    /// <summary>Every registered contributor, in registration order (the plan sorts the built stamps by priority).</summary>
    public static IReadOnlyList<WriteStampContributor> All
    {
        get { lock (_gate) return _contributors.ToArray(); }
    }

    /// <summary>
    /// Register a write contributor. Boot-only. Idempotent by <see cref="WriteStampContributor.Id"/> (a duplicate
    /// is a no-op, so a re-entrant Reference = Intent registrar is safe). Invalidates the per-type plan memo.
    /// </summary>
    public static void Register(WriteStampContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        if (string.IsNullOrWhiteSpace(contributor.Id))
            throw new ArgumentException("A write-stamp contributor must have a non-empty Id.", nameof(contributor));
        lock (_gate)
        {
            if (_contributors.Any(c => string.Equals(c.Id, contributor.Id, StringComparison.Ordinal)))
                return;
            _contributors.Add(contributor);
            _isEmpty = false;
        }
        StorageWritePlan.InvalidateCache();   // boot-only ⇒ rare; rebuilt plans now include this contributor
    }

    /// <summary>Test-support: clear all registrations and the plan memo (mirrors <c>ManagedFieldRegistry.Reset</c>).</summary>
    public static void Reset()
    {
        lock (_gate)
        {
            _contributors.Clear();
            _isEmpty = true;
        }
        StorageWritePlan.InvalidateCache();
    }
}
