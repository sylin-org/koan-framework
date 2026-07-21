using Koan.Data.Core;

namespace Koan.Identity.Management;

/// <summary>SEC-0007 Layer 2 — grant/revoke/list the GLOBAL roles bound to a person (idempotent via the deterministic id).</summary>
public sealed class IdentityRoleService
{
    public async Task<IdentityRole> GrantAsync(string identityId, string roleKey, CancellationToken ct = default)
    {
        var role = new IdentityRole
        {
            Id = IdentityRole.KeyFor(identityId, roleKey),
            IdentityId = identityId,
            RoleKey = roleKey,
        };
        return await role.Save(ct).ConfigureAwait(false);
    }

    public async Task<bool> RevokeAsync(string identityId, string roleKey, CancellationToken ct = default)
    {
        var role = await IdentityRole.Get(IdentityRole.KeyFor(identityId, roleKey), ct).ConfigureAwait(false);
        if (role is null) return false;
        await role.Remove(ct).ConfigureAwait(false);
        return true;
    }

    public Task<IReadOnlyList<IdentityRole>> ListAsync(string identityId, CancellationToken ct = default)
        => IdentityRole.Query(r => r.IdentityId == identityId, ct);
}
