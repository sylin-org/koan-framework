using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Koan.Jobs;

/// <summary>
/// ARCH-0100: the coalesce / idempotency identity must include the captured ambient. The "same work" for two
/// different tenants is <i>different</i> work — a tenant-blind coalesce key would let one tenant's idempotent submit
/// collapse onto another tenant's queued job (which would then run once, in the wrong tenant's ambient, against the
/// wrong tenant's data — bypassing the carrier guarantee at the submit gate, before any capture/restore).
///
/// <para>Folding the ambient bag into the stored <see cref="JobRecord.CoalesceKey"/> makes the dedup structurally
/// ambient-scoped (conformity-by-design): the dedup lookup compares the folded key, so no ledger query can forget
/// the filter — the key itself encodes the axis. An unscoped/system submit (null bag) keeps its global coalesce
/// identity. Axis-generic: this names no axis, it folds whatever the carrier captured.</para>
/// </summary>
internal static class JobCoalesce
{
    // A distinctive delimiter that brackets the ambient suffix so it cannot be confused with the base key.
    private const string AmbientMarker = "|@koan-ambient|";

    public static string? FoldAmbient(string? baseKey, IReadOnlyDictionary<string, string>? ambientCarrier)
    {
        if (baseKey is null) return null;                                          // no coalescing requested
        if (ambientCarrier is null || ambientCarrier.Count == 0) return baseKey;   // unscoped/system → global coalesce
        var sb = new StringBuilder(baseKey).Append(AmbientMarker);
        foreach (var kv in ambientCarrier.OrderBy(k => k.Key, StringComparer.Ordinal))   // deterministic, order-independent
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
        return sb.ToString();
    }
}
