using System.Security.Claims;
using Koan.Data.Core;
using Koan.Web.Authorization;
using Koan.Web.Hooks;

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

    public AccessExplainer(EffectiveAccessResolver resolver, IAuthorize? authorize = null)
    {
        _resolver = resolver;
        _authorize = authorize;
    }

    /// <summary>Reverse: the facts contributing to <paramref name="identityId"/>'s access on <paramref name="resourceName"/>.</summary>
    public async Task<IReadOnlyList<AccessFact>> WhyAsync(string identityId, string resourceName, CancellationToken ct = default)
    {
        var access = await _resolver.ResolveAsync(identityId, ct).ConfigureAwait(false);
        return access.Facts
            .Where(f => f.Resource == "*" || string.Equals(f.Resource, resourceName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

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
        var reason = decision is AuthorizeDecision.Forbid forbid ? forbid.Reason ?? "forbidden" : null;
        return new AccessDecision(allowed, reason, access.Facts);
    }

    /// <summary>One-click revoke: <c>Remove()</c> the contributing row.</summary>
    public async Task<bool> RevokeAsync(AccessFactRef fact, CancellationToken ct = default)
    {
        switch (fact.RowType)
        {
            case nameof(IdentityRole):
                var role = await IdentityRole.Get(fact.RowId, ct).ConfigureAwait(false);
                if (role is null) return false;
                await role.Remove(ct).ConfigureAwait(false);
                return true;
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
