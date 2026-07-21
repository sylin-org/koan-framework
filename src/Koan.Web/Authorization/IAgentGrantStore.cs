using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0005 — supplies the capabilities an agent has been granted (via <see cref="AgentGrant"/>) that are active and
/// apply to a given resource. Scoped: memoized per request so multi-action evaluations load once. The gate consults
/// it only when the token alone is denied; an absent store (or no grants) means "no grants" (backward-compatible).
/// </summary>
public interface IAgentGrantStore
{
    /// <summary>The active (unexpired) capability terms granted to <paramref name="subjectId"/> that apply to
    /// <paramref name="resourceName"/> (or the <c>"*"</c> wildcard). Empty when the subject is anonymous/unknown.</summary>
    Task<IReadOnlyList<string>> ActiveCapabilities(string subjectId, string resourceName, CancellationToken ct = default);
}
