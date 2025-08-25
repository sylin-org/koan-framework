using Microsoft.Extensions.Options;

namespace Sora.Web.Transformers;

internal sealed class TransformerRegistry : ITransformerRegistry
{
    private readonly Dictionary<Type, List<(string ContentType, object Transformer, int Priority)>> _map = new();
    private readonly IServiceProvider _sp;
    private readonly TransformerBindings _bindings;
    private bool _initialized;

    public TransformerRegistry(IServiceProvider sp, IOptions<TransformerBindings> bindings)
    {
        _sp = sp;
        _bindings = bindings.Value;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        // Execute deferred bindings to populate registry from DI
        foreach (var action in _bindings.Bindings.ToList()) action(_sp);
        _initialized = true;
    }

    public void Register<TEntity, TShape>(IEntityTransformer<TEntity, TShape> transformer, string[] contentTypes, int priority = (int)TransformerPriority.Discovered)
    {
        var key = typeof(TEntity);
        if (!_map.TryGetValue(key, out var list))
        {
            list = new List<(string, object, int)>();
            _map[key] = list;
        }
        foreach (var ct in contentTypes)
        {
            // remove any lower-priority registrations for the same content type
            var existing = list.FindIndex(x => string.Equals(x.ContentType, ct, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                if (priority > list[existing].Priority)
                    list[existing] = (ct, transformer!, priority);
                // if lower or equal, keep existing and skip
                continue;
            }
            list.Add((ct, transformer, priority));
        }
    }

    public TransformerMatch<TEntity>? ResolveForOutput<TEntity>(IEnumerable<string> acceptTypes)
    {
        EnsureInitialized();
        if (!_map.TryGetValue(typeof(TEntity), out var list)) return null;

        // Build candidate media ranges from Accept: type/subtype[;q=]
        var explicitRanges = new List<(string Type, string Sub, double Q, int Order)>();
        var wildcardRanges = new List<(string Type, string Sub, double Q, int Order)>();
        int order = 0;
        foreach (var accept in acceptTypes)
        {
            foreach (var raw in accept.Split(','))
            {
                var s = raw.Trim(); if (string.IsNullOrEmpty(s)) continue;
                var parts = s.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var media = parts[0]; var q = 1.0;
                for (int i = 1; i < parts.Length; i++)
                {
                    var kv = parts[i].Split('=', 2, StringSplitOptions.TrimEntries);
                    if (kv.Length == 2 && kv[0].Equals("q", StringComparison.OrdinalIgnoreCase))
                    {
                        if (double.TryParse(kv[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var qv)) q = qv;
                    }
                }
                var ts = media.Split('/'); if (ts.Length != 2) continue;
                var entry = (ts[0].ToLowerInvariant(), ts[1].ToLowerInvariant(), q, order++);
                if (ts[0] == "*" || ts[1] == "*") wildcardRanges.Add(entry); else explicitRanges.Add(entry);
            }
        }
        if (explicitRanges.Count == 0 && wildcardRanges.Count == 0) return null;

        // Prefer explicit Accepts; ignore */* only to allow default JSON to win
        (string ct, object tr, int prio, double score, int order)? best = null;
        bool TryScoreAgainst(IEnumerable<(string Type, string Sub, double Q, int Order)> ranges)
        {
            bool any = false;
            foreach (var reg in list)
            {
                var ts = reg.ContentType.Split('/'); if (ts.Length != 2) continue;
                var rType = ts[0].ToLowerInvariant(); var rSub = ts[1].ToLowerInvariant();
                foreach (var range in ranges)
                {
                    bool typeMatch = range.Type == "*" || range.Type == rType;
                    bool subMatch = range.Sub == "*" || range.Sub == rSub;
                    if (!typeMatch || !subMatch) continue;
                    any = true;
                    // Specificity: exact match (1.0), subtype wildcard (0.8), type wildcard (0.5)
                    double spec = (range.Type == "*" && range.Sub == "*") ? 0.5 : (range.Sub == "*" ? 0.8 : 1.0);
                    double score = range.Q * spec;
                    if (best is null || score > best.Value.score ||
                        (score == best.Value.score && reg.Priority > best.Value.prio) ||
                        (score == best.Value.score && reg.Priority == best.Value.prio && range.Order < best.Value.order))
                    {
                        best = (reg.ContentType, reg.Transformer, reg.Priority, score, range.Order);
                    }
                }
            }
            return any;
        }

        // Score explicit ranges first
        var hadExplicitMatch = TryScoreAgainst(explicitRanges);
        if (!hadExplicitMatch)
        {
            // If only wildcard Accepts (e.g., */*), do NOT force a transformer; let MVC default (JSON) handle it.
            // Only match wildcards if there was also at least one explicit media range.
            // This avoids surprising CSV when client doesn't ask for it.
            return null;
        }

        return best is not null ? new TransformerMatch<TEntity>(best.Value.ct, best.Value.tr) : null;
    }

    public TransformerMatch<TEntity>? ResolveForInput<TEntity>(string contentType)
    {
        EnsureInitialized();
        if (!_map.TryGetValue(typeof(TEntity), out var list)) return null;
        var ct = contentType.Split(';')[0].Trim();
        var match = list
            .Where(x => string.Equals(x.ContentType, ct, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Priority)
            .FirstOrDefault();
        if (match.Transformer is null) return null;
        return new TransformerMatch<TEntity>(match.ContentType, match.Transformer);
    }

    public IReadOnlyList<string> GetContentTypes<TEntity>()
    {
        EnsureInitialized();
        if (!_map.TryGetValue(typeof(TEntity), out var list)) return Array.Empty<string>();
        return list.Select(x => x.ContentType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}