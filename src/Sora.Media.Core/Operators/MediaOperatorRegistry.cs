using Sora.Storage.Abstractions;

namespace Sora.Media.Core.Operators;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Sora.Media.Core.Options;
using Sora.Storage;

public sealed class MediaOperatorRegistry : IMediaOperatorRegistry
{
    private readonly ILogger<MediaOperatorRegistry> _logger;
    private readonly IReadOnlyList<IMediaOperator> _operators;
    private readonly IOptionsMonitor<MediaTransformOptions> _options;
    private readonly Dictionary<string, List<IMediaOperator>> _aliasIndex; // alias -> ops

    public MediaOperatorRegistry(ILogger<MediaOperatorRegistry> logger, IEnumerable<IMediaOperator> operators, IOptionsMonitor<MediaTransformOptions> options)
    {
        _logger = logger;
        _operators = operators.ToList();
        _options = options;
        _aliasIndex = BuildAliasIndex(_operators);
    }

    public IReadOnlyList<IMediaOperator> Operators => _operators;

    public IMediaOperator? FindById(string id) => _operators.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<(IMediaOperator Op, IReadOnlyDictionary<string, string> Params)> ResolveOperators(IDictionary<string, StringValues> query, ObjectStat sourceStat, string? contentType, MediaTransformOptions? opt = null)
    {
        var options = opt ?? _options.CurrentValue;
        var strict = options.Strictness == MediaTransformStrictness.Strict;

        // First-wins duplicate handling into a working bag (lowercase keys)
        var bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in query)
        {
            var key = kvp.Key.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!bag.ContainsKey(key))
            {
                var v = kvp.Value.FirstOrDefault();
                if (v is not null) bag[key] = v.Trim();
            }
        }

        var selected = new List<(IMediaOperator Op, IReadOnlyDictionary<string, string> Params)>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Helper to test content-type support (prefix match allowed)
        bool Supports(IMediaOperator op, string? ct)
            => ct is null || op.SupportedContentTypes.Count == 0 || op.SupportedContentTypes.Any(s => s.EndsWith("/*") ? (ct.StartsWith(s[..^2], StringComparison.OrdinalIgnoreCase)) : string.Equals(s, ct, StringComparison.OrdinalIgnoreCase) || (s.EndsWith("/") && ct.StartsWith(s, StringComparison.OrdinalIgnoreCase)));

        // Single pass over keys to see which operator they map to via alias
        var encounteredOps = new HashSet<IMediaOperator>();
        foreach (var (k, v) in bag.ToArray())
        {
            if (!_aliasIndex.TryGetValue(k, out var ops)) continue;
            if (ops.Count > 1)
            {
                if (strict)
                    throw new InvalidOperationException($"Transform alias '{k}' overlaps between: {string.Join(", ", ops.Select(o => o.Id))}.");
                _logger.LogWarning("Media operator alias overlap for '{Alias}' across: {Ops}. Using precedence to disambiguate.", k, string.Join(", ", ops.Select(o => o.Id)));
            }

            // In relaxed mode, defer to precedence to choose one later; mark as potential
            foreach (var op in ops)
                encounteredOps.Add(op);
        }

        // Filter by content-type and keep only ops that actually have params after normalization
        var precedence = options.Precedence;
        var ordered = precedence.Select(id => FindById(id)).Where(op => op is not null).Cast<IMediaOperator>().ToList();
        // Append any others at the end deterministically
        ordered.AddRange(_operators.Where(o => !ordered.Contains(o)));

        foreach (var op in ordered)
        {
            if (!encounteredOps.Contains(op)) continue; // no relevant aliases in request
            if (!Supports(op, contentType)) continue;

            var normalized = op.Normalize(new Dictionary<string, StringValues>(query, StringComparer.OrdinalIgnoreCase), sourceStat, options, strict);
            if (normalized is null || normalized.Count == 0) continue;

            // Claim canonical keys to avoid reuse by subsequent ops
            foreach (var key in normalized.Keys)
                claimed.Add(key);

            selected.Add((op, normalized));
        }

        // Enforce placement: Terminal must be last; Pre must be before others
        var terminalCount = selected.Count(t => t.Op.Placement == MediaOperatorPlacement.Terminal);
        if (terminalCount > 1)
        {
            if (strict) throw new InvalidOperationException("Multiple Terminal operators selected.");
            _logger.LogWarning("Multiple Terminal operators selected; keeping only the last by precedence.");
            // Keep only the last
            var lastTerminal = selected.Select((t, idx) => (t, idx)).Last(x => x.t.Op.Placement == MediaOperatorPlacement.Terminal);
            selected = selected.Where((t, idx) => t.Op.Placement != MediaOperatorPlacement.Terminal || idx == lastTerminal.idx).ToList();
        }

        // Ensure Terminal, if present, is at the end
        var terminal = selected.FirstOrDefault(t => t.Op.Placement == MediaOperatorPlacement.Terminal);
        if (terminal.Op is not null && selected.Count > 0 && !ReferenceEquals(selected[^1].Op, terminal.Op))
        {
            if (strict)
                throw new InvalidOperationException("Terminal operator must be last.");
            selected = selected.Where(t => !ReferenceEquals(t.Op, terminal.Op)).Append(terminal).ToList();
        }

        return selected;
    }

    private static Dictionary<string, List<IMediaOperator>> BuildAliasIndex(IReadOnlyList<IMediaOperator> ops)
    {
        var index = new Dictionary<string, List<IMediaOperator>>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in ops)
        {
            foreach (var (canon, aliases) in op.ParameterAliases)
            {
                foreach (var a in aliases)
                {
                    var key = a.ToLowerInvariant();
                    if (!index.TryGetValue(key, out var list)) { list = []; index[key] = list; }
                    if (!list.Contains(op)) list.Add(op);
                }
                // Include canonical as alias of itself
                var self = canon.ToLowerInvariant();
                if (!index.TryGetValue(self, out var list2)) { list2 = []; index[self] = list2; }
                if (!list2.Contains(op)) list2.Add(op);
            }
        }
        return index;
    }
}
