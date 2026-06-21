using System;
using System.Collections.Generic;
using System.Linq;

namespace Koan.Core.Composition;

/// <summary>
/// Accumulator handed to <see cref="IKoanCompositionContributor"/>s so they can enrich the resolved
/// composition twin without depending on the internal <see cref="KoanLockfile"/> shape. Koan.Core
/// seeds app/modules/config-keys; pillars add elections, capabilities and entities. Last write wins
/// per key, so a later contributor can refine an earlier one.
/// </summary>
public sealed class KoanCompositionBuilder
{
    private readonly Dictionary<string, KoanLockElection> _elections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _capabilities = new(StringComparer.Ordinal);
    private readonly List<KoanLockEntity> _entities = new();
    private readonly HashSet<string> _configKeys = new(StringComparer.Ordinal);

    /// <summary>Record a resolved election, e.g. <c>data:default</c> → adapter <c>postgres</c>.</summary>
    public void AddElection(string key, string adapter, string via, int? priority = null)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(adapter)) return;
        _elections[key] = new KoanLockElection(adapter, via ?? "unknown", priority);
    }

    /// <summary>Record a provider's negotiated capability tokens, e.g. <c>data:postgres</c> → query.linq, …</summary>
    public void AddCapability(string owner, IEnumerable<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(owner) || tokens is null) return;
        var ordered = tokens.Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length > 0) _capabilities[owner] = ordered;
    }

    /// <summary>Record a Koan-namespaced configuration KEY consumed. Never pass a value.</summary>
    public void AddConfigKey(string key)
    {
        if (!string.IsNullOrWhiteSpace(key)) _configKeys.Add(key);
    }

    /// <summary>Record an entity and the traits it declares (e.g. <c>Embedding</c>).</summary>
    public void AddEntity(string type, IEnumerable<string>? traits = null)
    {
        if (string.IsNullOrWhiteSpace(type)) return;
        var list = traits?.Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
        _entities.Add(new KoanLockEntity(type, list));
    }

    /// <summary>Materialize the accumulated sections into the deterministic lockfile shape (internal).</summary>
    internal void ApplyTo(
        out IReadOnlyDictionary<string, KoanLockElection>? elections,
        out IReadOnlyDictionary<string, IReadOnlyList<string>>? capabilities,
        out IReadOnlyList<string>? configKeys,
        out IReadOnlyList<KoanLockEntity>? entities)
    {
        elections = _elections.Count == 0
            ? null
            : _elections.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        capabilities = _capabilities.Count == 0
            ? null
            : _capabilities.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        configKeys = _configKeys.Count == 0
            ? null
            : _configKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        entities = _entities.Count == 0
            ? null
            : _entities.OrderBy(e => e.Type, StringComparer.Ordinal).ToArray();
    }
}
