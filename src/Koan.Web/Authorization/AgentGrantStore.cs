using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0005 — the default <see cref="IAgentGrantStore"/>: reads active <see cref="AgentGrant"/> rows for a subject
/// FRESH per request (so <c>Remove()</c> / expiry take effect on the very next call — revocation with zero new
/// machinery), memoized WITHIN the request so a read+write+remove evaluation loads the subject's grants once. The query
/// asks for the subject's unexpired grants only, so expired rows are never returned, and a connector that builds the
/// <c>(Subject, ExpiresAt)</c> index reads only those; the grants that apply to the resource are kept.
/// </summary>
public sealed class AgentGrantStore : IAgentGrantStore
{
    private readonly Dictionary<string, IReadOnlyList<AgentGrant>> _bySubject = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<string>> ActiveCapabilities(string subjectId, string resourceName, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(subjectId)) return Array.Empty<string>();

        var now = DateTimeOffset.UtcNow;
        if (!_bySubject.TryGetValue(subjectId, out var grants))
        {
            grants = await AgentGrant.Query(g => g.Subject == subjectId && (g.ExpiresAt == null || g.ExpiresAt > now), ct)
                .ConfigureAwait(false);
            _bySubject[subjectId] = grants;
        }

        return grants
            .Where(g => g.IsActive(now) && g.AppliesTo(resourceName))
            .Select(g => g.Capability)
            .ToList();
    }
}
