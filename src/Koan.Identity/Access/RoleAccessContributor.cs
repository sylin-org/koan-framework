using Koan.Identity.Roles;

namespace Koan.Identity.Access;

/// <summary>
/// Contributes the person's server roles and their global permissions to the administrative access view: the roles
/// they are members of, as role facts revocable by membership, and the permissions of the implicit <c>everyone</c>
/// and <c>authenticated</c> roles, which reach every person's bag without a membership. The implicit roles are never
/// role facts: a sign-in stamps no role claim for them, so a principal simulated from these facts carries the role
/// claims a real sign-in carries.
/// </summary>
internal sealed class RoleAccessContributor(RoleCollection roles) : IEffectiveAccessContributor
{
    private const string Source = "Role";
    private const string MembershipRow = "RoleMember";

    public async Task<IReadOnlyList<AccessFact>> ContributeAsync(string identityId, CancellationToken ct = default)
    {
        var assigned = await roles.ForPerson(identityId, ct).ConfigureAwait(false);
        var facts = assigned.Where(role => !RoleContract.IsImplicitRole(role.Id))
            .SelectMany(role => Permissions(role, MembershipRow, identityId)
                .Prepend(new AccessFact(Source, AccessFactKind.Role, role.Id, "*", AccessFactScope.Global, MembershipRow, role.Id, null, identityId)))
            .ToList();
        foreach (var key in new[] { RoleTokens.Everyone, RoleTokens.Authenticated })
        {
            if (await roles.Get(key, ct).ConfigureAwait(false) is { } implicitRole) facts.AddRange(Permissions(implicitRole, nameof(Role), null));
        }

        return facts;
    }

    private static IEnumerable<AccessFact> Permissions(Role role, string rowType, string? subject)
        => role.Permissions.Where(value => value.StartsWith(RoleTokens.GlobalPrefix, StringComparison.Ordinal))
            .Select(value => new AccessFact(Source, AccessFactKind.Permission, value, "*", AccessFactScope.Global, rowType, role.Id, null, subject));
}
