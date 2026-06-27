using System.Linq;
using System.Threading.Tasks;
using Koan.Data.Core;

namespace Koan.Tenancy;

/// <summary>
/// First-owner onboarding (ARCH-0099 §2) — the one-shot bootstrap that makes the first user the Owner of
/// <b>their own</b> tenant (a role-on-membership, zero cross-tenant reach), never the rejected cross-tenant
/// master. Idempotent: once a tenant has an Owner, a second claim is ignored (Keycloak's model). The durable ops
/// run against the <c>[HostScoped]</c> control-plane entities, so they need no tenant in scope. The caller
/// authorizes the claim via <see cref="TenantBootstrapPolicy"/> first.
/// </summary>
public static class TenantBootstrap
{
    /// <summary>True when <paramref name="tenantId"/> already has an Owner membership.</summary>
    public static async Task<bool> IsOwnerClaimedAsync(string tenantId)
        => (await Membership.Query(m => m.TenantId == tenantId)).Any(m => m.IsOwner);

    /// <summary>
    /// Claim Owner of <paramref name="tenantId"/> for <paramref name="identityId"/> — one-shot: if an Owner
    /// already exists, returns it unchanged; otherwise creates the Owner membership. A concurrent double-claim by the
    /// SAME identity converges to one row via the deterministic <see cref="Membership.KeyFor"/> seat id (both upsert
    /// the same id); a race between two DIFFERENT identities is still gated rare by the prod claim window.
    /// </summary>
    public static async Task<Membership> ClaimOwnerAsync(string tenantId, string identityId)
    {
        var existingOwner = (await Membership.Query(m => m.TenantId == tenantId)).FirstOrDefault(m => m.IsOwner);
        if (existingOwner is not null) return existingOwner;
        return await new Membership
        {
            Id = Membership.KeyFor(tenantId, identityId),
            TenantId = tenantId,
            IdentityId = identityId,
            Roles = { TenancyRoles.Owner },
        }.Save();
    }

    /// <summary>Ensure a tenant with <paramref name="id"/> exists (idempotent); creates it named <paramref name="name"/> if absent.</summary>
    public static async Task<TenantRecord> EnsureTenantAsync(string id, string name)
        => await TenantRecord.Get(id) ?? await new TenantRecord { Id = id, Name = name }.Save();

    /// <summary>
    /// Ensure the dev tenant and its Owner exist (idempotent) — graduates the in-memory dev seed
    /// (<see cref="TenancyDevState"/>) onto durable rows, so the control-plane console has real data to show.
    /// </summary>
    public static async Task<(TenantRecord Tenant, Membership Owner)> EnsureDevAsync(string tenantId, string name, string ownerIdentity)
    {
        var tenant = await EnsureTenantAsync(tenantId, name);
        var owner = await ClaimOwnerAsync(tenantId, ownerIdentity);
        return (tenant, owner);
    }
}
