using Koan.Identity.Roles;

namespace Koan.Identity.Access;

/// <summary>
/// Contributes the person's server roles and their global permissions to the administrative access view: the roles
/// they are members of, as role facts whose row is the <see cref="RoleMember"/> a revoke removes, and the permissions of
/// the implicit <c>everyone</c> and <c>authenticated</c> roles, which reach every person's bag without a membership. The
/// implicit roles are never role facts: a request projects no role claim for them, so a principal simulated from these
/// facts carries the role claims a real request carries.
/// </summary>
internal sealed class RoleAccessContributor(RoleCollection roles) : IEffectiveAccessContributor
{
    private const string Source = "Role";

    public async Task<IReadOnlyList<AccessFact>> ContributeAsync(string identityId, CancellationToken ct = default)
    {
        var person = RoleContract.Require(identityId, nameof(identityId));
        var assigned = await roles.ForPerson(person, ct).ConfigureAwait(false);
        var facts = assigned.Where(role => !RoleContract.IsImplicitRole(role.Key))
            .SelectMany(role =>
            {
                var membership = RoleMember.KeyFor(role.Key, person);
                return Permissions(role, nameof(RoleMember), membership, person)
                    .Prepend(new AccessFact(Source, AccessFactKind.Role, role.Key, "*", AccessFactScope.Global, nameof(RoleMember), membership, null, person));
            })
            .ToList();
        foreach (var key in new[] { RoleTokens.Everyone, RoleTokens.Authenticated })
        {
            if (await roles.Get(key, ct).ConfigureAwait(false) is { } implicitRole) facts.AddRange(Permissions(implicitRole, nameof(Role), implicitRole.Id, null));
        }

        return facts;
    }

    private static IEnumerable<AccessFact> Permissions(Role role, string rowType, string rowId, string? subject)
        => role.Permissions.Where(value => value.StartsWith(RoleTokens.GlobalPrefix, StringComparison.Ordinal))
            .Select(value => new AccessFact(Source, AccessFactKind.Permission, value, "*", AccessFactScope.Global, rowType, rowId, null, subject));
}
