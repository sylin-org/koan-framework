using Koan.Identity.Roles;

namespace Koan.Identity.Access;

/// <summary>Contributes direct server-role membership and its global grants to the administrative access view.</summary>
internal sealed class RoleAccessContributor(RoleCollection roles) : IEffectiveAccessContributor
{
    public async Task<IReadOnlyList<AccessFact>> ContributeAsync(string identityId, CancellationToken ct = default)
    {
        var assigned = await roles.ForPerson(identityId, ct).ConfigureAwait(false);
        return assigned.SelectMany(role =>
            new[] { new AccessFact("Role", "role", role.Id, "*", "global", "RoleMember", role.Id, null, identityId) }
                .Concat(role.Permissions.Where(value => value.StartsWith(RoleTokens.GlobalPrefix, StringComparison.Ordinal))
                    .Select(value => new AccessFact("Role", "permission", value, "*", "global", "RoleMember", role.Id, null, identityId))))
            .ToList();
    }
}
