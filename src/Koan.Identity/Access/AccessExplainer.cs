using System.Security.Claims;
using Koan.Data.Core;
using Koan.Web.Authorization;
using Koan.Web.Hooks;
using Koan.Identity.Roles;

namespace Koan.Identity.Access;

/// <summary>
/// SEC-0007 Layer 2 — the bidirectional access explainer + one-click revoke. Reverse: "why does X have access to
/// Z?" → the exact contributing rows. Forward: "can X do action on Z?" runs the SAME authorize engine production
/// uses (preview == production). Revoke = <c>Remove()</c> on the contributing row.
/// </summary>
public sealed class AccessExplainer
{
    private readonly EffectiveAccessResolver _resolver;
    private readonly IAuthorize? _authorize;
    private readonly RoleCollection? _roles;

    public AccessExplainer(EffectiveAccessResolver resolver, IAuthorize? authorize = null, RoleCollection? roles = null)
    {
        _resolver = resolver;
        _authorize = authorize;
        _roles = roles;
    }

    /// <summary>
    /// Reverse: the facts contributing to <paramref name="identityId"/>'s access on <paramref name="resourceName"/> — the
    /// identity-wide facts that hold there, and the overwrites that apply to the person on that resource, each allowed
    /// permission a <see cref="AccessFactKind.Permission"/> fact and each withheld one a <see cref="AccessFactKind.Deny"/>
    /// fact naming its row. An overwrite holds on one resource, so it is read for the resource asked about, never
    /// across every resource the person can reach.
    /// </summary>
    public async Task<IReadOnlyList<AccessFact>> WhyAsync(string identityId, string resourceName, CancellationToken ct = default)
    {
        resourceName = RoleContract.Require(resourceName, nameof(resourceName));
        var access = await _resolver.ResolveAsync(identityId, ct).ConfigureAwait(false);
        var facts = access.Facts.Where(f => AppliesTo(f, resourceName)).ToList();
        if (_roles is null) return facts;

        var bag = await _roles.Bag(identityId, authenticated: true, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        facts.AddRange((await _roles.OverwritesFor(bag, resourceName, ct).ConfigureAwait(false))
            .Where(row => row.AppliesAt(now))
            .SelectMany(row => row.Allow.Select(permission => row.Fact(AccessFactKind.Permission, permission))
                .Concat(row.Deny.Select(permission => row.Fact(AccessFactKind.Deny, permission)))));
        return facts;
    }

    /// <summary>
    /// Whether a fact holds on <paramref name="resourceName"/>: a wildcard fact everywhere; an agent grant as the gate
    /// matches it (<see cref="AgentGrant.AppliesTo"/>, entity names without case); every other resource id, opaque,
    /// exactly as resolution compares it.
    /// </summary>
    private static bool AppliesTo(AccessFact fact, string resourceName)
        => fact.Resource == "*" || string.Equals(fact.Resource, resourceName,
            fact.RowType == nameof(AgentGrant) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Forward: "can X do <paramref name="action"/> on <paramref name="resource"/>?" via the SAME
    /// <see cref="IAuthorize"/> the production gate uses. The simulated principal carries X's subject id (so the
    /// floor looks up X's real agent grants by subject) and X's effective role claims (global + tenant). The
    /// decision is therefore production-faithful: it reflects the floor's allow-by-default unless a role/grant/policy
    /// or entity <c>[Access]</c> rule denies. Pass an entity type/instance as <paramref name="resource"/> to evaluate
    /// entity-level <c>[Access]</c>; a bare string reflects the role/policy decision.
    /// </summary>
    public async Task<AccessDecision> CanAsync(string identityId, string action, object? resource = null, CancellationToken ct = default)
    {
        var access = await _resolver.ResolveAsync(identityId, ct).ConfigureAwait(false);
        if (_authorize is null)
            return new AccessDecision(false, "no authorize engine is registered", access.Facts);

        // The subject id lets the floor resolve X's real grants (AgentGrantStore is keyed by subject); the role
        // claims reproduce X's effective roles. Together this is what production would evaluate for X.
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, identityId) };
        claims.AddRange(access.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "explainer"));

        var decision = await _authorize.AuthorizeAsync(
            new AuthorizeRequest { Subject = principal, Action = action, Resource = resource }, ct).ConfigureAwait(false);

        var allowed = decision is AuthorizeDecision.Allow;
        var reason = decision switch
        {
            AuthorizeDecision.Allow => null,
            AuthorizeDecision.Forbid forbid => forbid.Reason ?? "forbidden",
            AuthorizeDecision.Refuse refuse => refuse.Reason.Text,
            _ => null,
        };
        return new AccessDecision(allowed, reason, access.Facts);
    }

    /// <summary>
    /// "Can X use <paramref name="permission"/> on <paramref name="resourceId"/>, and if not, why?" Resolves X's cached
    /// bag against the overwrites that apply to it there (<see cref="RoleCollection.OverwritesFor"/>, cached) with
    /// <see cref="RoleBag.On"/> and returns the deciding fact: the overwrite whose deny withheld the permission,
    /// revocable through its <see cref="AccessFactRef"/>, or the fact that no role X holds grants it.
    /// </summary>
    public async Task<AccessDecision> Explain(string identityId, string resourceId, string permission, CancellationToken ct = default)
    {
        if (_roles is null) return new AccessDecision(false, "no role collection is registered", []);
        var bag = await _roles.Bag(identityId, authenticated: true, ct).ConfigureAwait(false);
        var overwrites = await _roles.OverwritesFor(bag, resourceId, ct).ConfigureAwait(false);
        return bag.On(resourceId, overwrites, DateTimeOffset.UtcNow).Explain(permission);
    }

    /// <summary>
    /// One-click revoke of exactly the contributing fact: a role membership, an agent grant, or one permission of an
    /// overwrite (<see cref="AccessFact.Ref"/>), which removes the overwrite only when nothing is left in it.
    /// </summary>
    public async Task<bool> RevokeAsync(AccessFactRef fact, CancellationToken ct = default)
    {
        switch (fact.RowType)
        {
            case nameof(RoleMember):
                if (_roles is null || !RoleContract.TryRequire(fact.RowId, out var membershipId)) return false;
                return await RoleMember.Get(membershipId, ct).ConfigureAwait(false) is { } member &&
                       await _roles.Remove(member.RoleKey, member.PersonId, ct).ConfigureAwait(false);
            case nameof(PermissionOverwrite):
                // One row holds several facts: removing the row for one of them would also lift its denies. Only the
                // named permission leaves the named list; a handle that names no valid permission revokes nothing.
                if (_roles is null || fact.Kind is not (AccessFactKind.Permission or AccessFactKind.Deny)) return false;
                return await _roles.RevokeFromOverwrite(fact.RowId, fact.Kind == AccessFactKind.Deny, fact.Value, ct).ConfigureAwait(false);
            case nameof(AgentGrant):
                var grant = await AgentGrant.Get(fact.RowId, ct).ConfigureAwait(false);
                if (grant is null) return false;
                await grant.Remove(ct).ConfigureAwait(false);
                return true;
            default:
                return false;
        }
    }
}

/// <summary>The result of a forward "can X do Y on Z?" explanation.</summary>
public sealed record AccessDecision(bool Allowed, string? Reason, IReadOnlyList<AccessFact> ContributingFacts);
