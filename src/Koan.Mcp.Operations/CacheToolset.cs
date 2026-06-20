using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Stores;

namespace Koan.Mcp.Operations;

/// <summary>
/// P3.2 — the <c>cache</c> operational toolset (opt-in via <c>Koan:Mcp:Operations:Cache</c>). Governed agent verbs
/// over the cache: flush one entity type's entries, or flush the whole cache. All require an <c>@ops:cache</c> grant;
/// both verbs audit; <c>flushAll</c> is destructive (needs <c>confirm</c>). Wraps <see cref="ICacheClient"/> directly
/// (the generic entity-cache handle is type-parameterized). There is no global cache-clear primitive, so
/// <c>flushAll</c> enumerates the registered <see cref="ICachePolicyRegistry"/> entity tags.
/// </summary>
[McpOperationalToolset("cache")]
public sealed class CacheToolset : Toolset
{
    private readonly ICacheClient _cache;
    private readonly ICachePolicyRegistry _policies;

    public CacheToolset(ICacheClient cache, ICachePolicyRegistry policies)
    {
        _cache = cache;
        _policies = policies;
    }

    [McpTool(Name = "koan.cache.flush", Description = "Flush all cache entries for one entity type (by name). Requires an @ops:cache grant.", IsMutation = true)]
    public async Task<object> Flush(string entity, ClaimsPrincipal? user, CancellationToken ct)
    {
        var subject = await OpsGate.RequireGrant(user, "cache", ct);
        var flushed = await _cache.FlushTags(new[] { entity }, ct);
        await OpsGate.Audit(subject, "cache", "flush", entity);
        return new { entity, flushed };
    }

    [McpTool(Name = "koan.cache.flushAll", Description = "Flush the ENTIRE cache (every cacheable entity type). Destructive — pass confirm:true. Requires an @ops:cache grant.", IsMutation = true)]
    [McpDestructive]
    public async Task<object> FlushAll(ClaimsPrincipal? user, CancellationToken ct, bool confirm = false)
    {
        var subject = await OpsGate.RequireGrant(user, "cache", ct);

        var tags = _policies.GetAllPolicies()
            .Select(p => p.DeclaringType?.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!confirm) return OpsGate.DryRun($"flush the entire cache — all entries across {tags.Length} cacheable entity type(s)");

        var flushed = tags.Length == 0 ? 0L : await _cache.FlushTags(tags, ct);
        await OpsGate.Audit(subject, "cache", "flushAll", "");
        return new { flushed, entityTypes = tags.Length };
    }
}
