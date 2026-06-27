using Koan.Data.Core;
using Koan.Identity.Access;
using Koan.Tenancy;

namespace Koan.Identity.Tenancy.Access;

/// <summary>
/// SEC-0007 P2/P4 — the Membership contributor the Layer-2 <c>EffectiveAccessResolver</c> already expects (it ships
/// only with this Identity × Tenancy bridge — graceful degradation, no code-path fork). Contributes the roles a
/// person holds in the <b>ambient</b> tenant as access facts, over the SAME resolver / explainer as the global-role
/// and agent-grant contributors. Mirrors <c>AgentGrantAccessContributor</c>: tenant-scoped, so it degrades to empty
/// in the global (no-tenant) operator view and lights up inside a <c>Tenant.Use(...)</c> scope.
/// </summary>
internal sealed class MembershipAccessContributor : IEffectiveAccessContributor
{
    public async Task<IReadOnlyList<AccessFact>> ContributeAsync(string identityId, CancellationToken ct = default)
    {
        // Membership roles are scoped to the ambient tenant; with no tenant in scope (the global effective-access /
        // operator view) there is no membership plane to read — degrade to empty (the global-role + grant
        // contributors still answer). Inside Tenant.Use(...) the membership roles light up.
        var tenantId = Tenant.Current?.Id;
        if (string.IsNullOrEmpty(tenantId)) return Array.Empty<AccessFact>();

        // Membership is [HostScoped] (host plane), so this read is correct inside or outside a tenant scope; we filter
        // to the ambient tenant explicitly.
        var memberships = await Membership.Query(m => m.IdentityId == identityId && m.TenantId == tenantId, ct).ConfigureAwait(false);
        return memberships
            .SelectMany(m => m.Roles.Select(role =>
                new AccessFact("Membership", "role", role, "*", tenantId, nameof(Membership), m.Id, null)))
            .ToList();
    }
}
