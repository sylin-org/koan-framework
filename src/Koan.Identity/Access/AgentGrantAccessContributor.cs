using Koan.Data.Core;
using Koan.Web.Authorization;

namespace Koan.Identity.Access;

/// <summary>Contributes the SEC-0005 <see cref="AgentGrant"/> capabilities a subject holds (per-resource, time-boxed).</summary>
internal sealed class AgentGrantAccessContributor : IEffectiveAccessContributor
{
    public async Task<IReadOnlyList<AccessFact>> ContributeAsync(string identityId, CancellationToken ct = default)
    {
        IReadOnlyList<AgentGrant> grants;
        try
        {
            grants = await AgentGrant.Query(g => g.Subject == identityId, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // AgentGrant is tenant-scoped (SEC-0005). In a no-tenant context (the GLOBAL operator/effective-access
            // view under a Closed posture) the fail-closed axis guard fires — there are simply no tenant grants to
            // contribute, so degrade to empty rather than fail the whole resolution. Inside a tenant scope the grants
            // light up. The grants' Scope is the ambient tenant, not "global".
            return Array.Empty<AccessFact>();
        }

        // Match the production gate (AgentGrantStore.ActiveCapabilities filters on IsActive) so the access preview
        // never shows an expired grant as live access.
        var now = DateTimeOffset.UtcNow;
        return grants
            .Where(g => g.IsActive(now))
            .Select(g => new AccessFact("AgentGrant", "capability", g.Capability, g.Resource, "tenant", nameof(AgentGrant), g.Id, g.ExpiresAt))
            .ToList();
    }
}
