using Koan.Data.Core;

namespace Koan.Identity.Access;

/// <summary>Contributes the GLOBAL roles bound to a person (the no-tenancy binding).</summary>
internal sealed class IdentityRoleAccessContributor : IEffectiveAccessContributor
{
    public async Task<IReadOnlyList<AccessFact>> ContributeAsync(string identityId, CancellationToken ct = default)
    {
        var roles = await IdentityRole.Query(r => r.IdentityId == identityId, ct).ConfigureAwait(false);
        return roles
            .Select(r => new AccessFact("IdentityRole", "role", r.RoleKey, "*", "global", nameof(IdentityRole), r.Id, null))
            .ToList();
    }
}
