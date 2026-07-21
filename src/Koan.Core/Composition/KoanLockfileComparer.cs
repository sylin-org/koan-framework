using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Core.Diagnostics;
using Koan.Core.Infrastructure;

namespace Koan.Core.Composition;

internal enum KoanLockStatus
{
    /// <summary>No checked-in lockfile to compare against (greenfield / first build).</summary>
    NoLockfile,
    /// <summary>The checked-in lockfile matches the resolved composition.</summary>
    Ok,
    /// <summary>The resolved composition diverged from the checked-in lockfile.</summary>
    Drift,
}

internal readonly record struct KoanLockComparison(KoanLockStatus Status, IReadOnlyList<string> DriftKeys)
{
    /// <summary>The one-line boot-report verdict: <c>&lt;n&gt; modules · lockfile ok|DRIFT(keys)|not found</c>.</summary>
    public string Format(int moduleCount)
    {
        var noun = moduleCount == 1 ? "module" : "modules";
        return Status switch
        {
            KoanLockStatus.Ok => $"{moduleCount} {noun} · lockfile ok",
            KoanLockStatus.Drift => $"{moduleCount} {noun} · lockfile DRIFT({string.Join(", ", DriftKeys)})",
            _ => $"{moduleCount} {noun} · lockfile not found",
        };
    }

    public KoanFact ToFact(int moduleCount)
    {
        var (code, kind, state, reason, correction) = Status switch
        {
            KoanLockStatus.Ok => (
                Constants.Diagnostics.Codes.LockfileMatched,
                KoanFactKind.Dependency,
                KoanFactState.Healthy,
                Constants.Diagnostics.Reasons.LockfileMatched,
                (string?)null),
            KoanLockStatus.Drift => (
                Constants.Diagnostics.Codes.LockfileDrifted,
                KoanFactKind.Degradation,
                KoanFactState.Degraded,
                Constants.Diagnostics.Reasons.LockfileDrifted,
                "Regenerate and review koan.lock.json so build intent matches the resolved runtime composition."),
            _ => (
                Constants.Diagnostics.Codes.LockfileMissing,
                KoanFactKind.Dependency,
                KoanFactState.Unknown,
                Constants.Diagnostics.Reasons.LockfileMissing,
                "Build the application with Koan package targets enabled to create koan.lock.json.")
        };

        return KoanFact.Create(
            code,
            kind,
            state,
            "koan.lock.json",
            Format(moduleCount),
            reason,
            correction,
            nameof(KoanLockfileComparer),
            "composition:lockfile");
    }
}

/// <summary>
/// Compares a checked-in lockfile against the boot-resolved composition. Schema, app identity, the module
/// set (id + major.minor), and direct-reference provenance are compared. The richer sections — elections, capabilities, config
/// keys, entities — are compared ONLY when both files carry them: the build-time file omits them (they
/// are runtime-resolved), so the boot-line comparison stays static-composition-clean, while a full-vs-full
/// comparison (two resolved twins) surfaces election/capability/key drift too. Drift keys read as a
/// diff: <c>+X</c> appeared at runtime, <c>-X</c> is locked but absent, <c>X@v</c> changed.
/// </summary>
internal static class KoanLockfileComparer
{
    public static KoanLockComparison Compare(KoanLockfile? locked, KoanLockfile resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        if (locked is null) return new KoanLockComparison(KoanLockStatus.NoLockfile, Array.Empty<string>());

        var keys = new List<string>();

        if (locked.Schema != resolved.Schema) keys.Add("schema");
        if (!AppMatches(locked.App, resolved.App)) keys.Add("app");

        DiffMap(
            keys, prefix: null,
            locked.Modules.ToDictionary(m => m.Id, m => m.Version, StringComparer.Ordinal),
            resolved.Modules.ToDictionary(m => m.Id, m => m.Version, StringComparer.Ordinal));

        if (locked.DirectReferences is not null || resolved.DirectReferences is not null)
            DiffSet(
                keys,
                "reference:",
                (locked.DirectReferences ?? Array.Empty<KoanLockReference>()).Select(ReferenceKey),
                (resolved.DirectReferences ?? Array.Empty<KoanLockReference>()).Select(ReferenceKey));

        // Richer sections: compared only when BOTH sides declare them (different tiers otherwise).
        if (locked.Elections is { } le && resolved.Elections is { } re)
            DiffMap(keys, "election:",
                le.ToDictionary(kv => kv.Key, kv => $"{kv.Value.Adapter}/{kv.Value.Via}", StringComparer.Ordinal),
                re.ToDictionary(kv => kv.Key, kv => $"{kv.Value.Adapter}/{kv.Value.Via}", StringComparer.Ordinal));

        if (locked.Capabilities is { } lc && resolved.Capabilities is { } rc)
            DiffMap(keys, "capability:",
                lc.ToDictionary(kv => kv.Key, kv => string.Join(",", kv.Value), StringComparer.Ordinal),
                rc.ToDictionary(kv => kv.Key, kv => string.Join(",", kv.Value), StringComparer.Ordinal));

        if (locked.ConfigKeys is { } lk && resolved.ConfigKeys is { } rk)
            DiffSet(keys, "configKey:", lk, rk);

        if (locked.Entities is { } len && resolved.Entities is { } ren)
            DiffMap(keys, "entity:",
                len.ToDictionary(e => e.Type, e => string.Join(",", e.Traits), StringComparer.Ordinal),
                ren.ToDictionary(e => e.Type, e => string.Join(",", e.Traits), StringComparer.Ordinal));

        return keys.Count == 0
            ? new KoanLockComparison(KoanLockStatus.Ok, Array.Empty<string>())
            : new KoanLockComparison(KoanLockStatus.Drift, keys);
    }

    private static void DiffMap(List<string> keys, string? prefix, Dictionary<string, string> locked, Dictionary<string, string> resolved)
    {
        var p = prefix ?? "";
        foreach (var id in resolved.Keys.Where(id => !locked.ContainsKey(id)).OrderBy(x => x, StringComparer.Ordinal))
            keys.Add($"+{p}{id}");
        foreach (var id in locked.Keys.Where(id => !resolved.ContainsKey(id)).OrderBy(x => x, StringComparer.Ordinal))
            keys.Add($"-{p}{id}");
        foreach (var id in resolved.Keys.Where(locked.ContainsKey).OrderBy(x => x, StringComparer.Ordinal))
            if (!string.Equals(locked[id], resolved[id], StringComparison.Ordinal))
                keys.Add($"{p}{id}@{resolved[id]}");
    }

    private static void DiffSet(List<string> keys, string prefix, IEnumerable<string> locked, IEnumerable<string> resolved)
    {
        var l = new HashSet<string>(locked, StringComparer.Ordinal);
        var r = new HashSet<string>(resolved, StringComparer.Ordinal);
        foreach (var k in r.Where(k => !l.Contains(k)).OrderBy(x => x, StringComparer.Ordinal)) keys.Add($"+{prefix}{k}");
        foreach (var k in l.Where(k => !r.Contains(k)).OrderBy(x => x, StringComparer.Ordinal)) keys.Add($"-{prefix}{k}");
    }

    private static bool AppMatches(KoanLockApp a, KoanLockApp b)
        => string.Equals(a.Name, b.Name, StringComparison.Ordinal)
        && string.Equals(a.Koan, b.Koan, StringComparison.Ordinal)
        && string.Equals(a.Tfm, b.Tfm, StringComparison.Ordinal);

    private static string ReferenceKey(KoanLockReference reference) => $"{reference.Kind}:{reference.Id}";
}
