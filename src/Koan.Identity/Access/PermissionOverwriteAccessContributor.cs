using Koan.Identity.Roles;

namespace Koan.Identity.Access;

/// <summary>
/// Contributes the overwrites that apply to a person — on everyone, on the roles their bag holds, and on them — as
/// facts on their resource: each allowed permission as a <see cref="AccessFactKind.Permission"/> fact and each
/// withheld one as a <see cref="AccessFactKind.Deny"/> fact. Every fact names its overwrite row, so a revoke removes
/// the overwrite. An overwrite that has expired contributes nothing.
/// </summary>
internal sealed class PermissionOverwriteAccessContributor(RoleCollection roles) : IEffectiveAccessContributor
{
    public async Task<IReadOnlyList<AccessFact>> ContributeAsync(string identityId, CancellationToken ct = default)
    {
        var bag = await roles.Bag(identityId, authenticated: true, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        return (await roles.OverwritesFor(bag, ct).ConfigureAwait(false))
            .Where(row => row.AppliesAt(now))
            .SelectMany(row => row.Allow.Select(permission => row.Fact(AccessFactKind.Permission, permission))
                .Concat(row.Deny.Select(permission => row.Fact(AccessFactKind.Deny, permission))))
            .ToList();
    }
}
