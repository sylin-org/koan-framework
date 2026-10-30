using System.Security.Claims;
using Koan.Tenancy;
using Koan.Web.Context;
using Microsoft.Extensions.Options;

namespace Koan.Identity.Tenancy;

/// <summary>
/// Resolves untrusted request tenant evidence, authorizes it against current durable membership and person state,
/// projects tenant roles, and contributes the ambient tenant for later Web context contributors and endpoints.
/// </summary>
internal sealed class TenantResolutionContributor(
    IEnumerable<ITenantResolver> resolvers,
    IOptions<TenancyResolutionOptions> options) : IWebContextContributor
{
    private readonly ILookup<TenantCarrierRank, ITenantResolver> _ranks = resolvers.ToLookup(static resolver => resolver.Rank);

    public int Order => 100;

    public async ValueTask ContributeAsync(WebContext webContext)
    {
        var context = webContext.HttpContext;
        var subject = webContext.SubjectId;

        // Anonymous requests cannot be scoped in. Avoid carrier/control-plane work entirely.
        if (string.IsNullOrEmpty(subject)) return;

        var opts = options.Value;
        var request = new TenantResolutionRequest(
            Host: context.Request.Host.Host,
            Path: context.Request.Path.Value,
            Subject: subject,
            Claim: type => context.User.FindFirst(type)?.Value,
            Header: name => context.Request.Headers.TryGetValue(name, out var value) ? value.ToString() : null);

        var (candidate, disagreed) = await ResolveCandidate(request, context.RequestAborted).ConfigureAwait(false);
        if (disagreed)
        {
            // Two carriers of one rank name different tenants: nothing says which the person means. Existence-hiding
            // like every other refusal of a tenant the person cannot reach.
            webContext.Reject();
            return;
        }
        if (string.IsNullOrEmpty(candidate)) return;

        // One query authorizes the candidate and supplies the roles to project.
        var memberships = await Membership.Query(
            membership => membership.IdentityId == subject && membership.TenantId == candidate,
            context.RequestAborted).ConfigureAwait(false);
        if (memberships.Count == 0) return;

        // A stale membership or principal never restores a deactivated durable person.
        var person = await global::Koan.Identity.Identity.Get(subject, context.RequestAborted).ConfigureAwait(false);
        if (person is not { IsActive: true }) return;

        if (ProjectRoles(context.User, memberships) is { } augmented)
            webContext.UsePrincipal(augmented);
        webContext.Use(() => Tenant.Use(candidate));
    }

    /// <summary>
    /// The tenant the request names: the lowest rank that names one decides (<see cref="TenantCarrierRank"/>), and every
    /// carrier of that rank must agree. Ranks below it are not read, so what the address decides costs no other carrier.
    /// </summary>
    private async ValueTask<(string? Candidate, bool Disagreed)> ResolveCandidate(TenantResolutionRequest request, CancellationToken ct)
    {
        foreach (var rank in Enum.GetValues<TenantCarrierRank>())
        {
            string? named = null;
            foreach (var resolver in _ranks[rank])
            {
                var candidate = await resolver.ResolveAsync(request, ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(candidate)) continue;
                if (named is not null && !StringComparer.Ordinal.Equals(named, candidate)) return (null, true);
                named = candidate;
            }

            if (named is not null) return (named, false);
        }

        return (null, false);
    }

    /// <summary>Project tenant roles without ever conferring a reserved host role.</summary>
    internal static ClaimsPrincipal? ProjectRoles(ClaimsPrincipal principal, IReadOnlyList<Membership> memberships)
    {
        var roles = memberships
            .SelectMany(static membership => membership.Roles)
            .Where(role => !string.IsNullOrWhiteSpace(role)
                           && !TenancyRoles.IsReservedHostRole(role)
                           && !IdentityRoles.IsReservedHostRole(role)
                           && !principal.IsInRole(role))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (roles.Count == 0) return null;

        var clone = new ClaimsPrincipal(principal.Identities);
        clone.AddIdentity(new ClaimsIdentity(roles.Select(static role => new Claim(ClaimTypes.Role, role))));
        return clone;
    }

    /// <summary>True when a subject holds a Membership in the given tenant. Anonymous is never a member.</summary>
    internal static async Task<bool> IsMemberAsync(string? subject, string tenantId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(subject)) return false;
        var memberships = await Membership.Query(
            membership => membership.IdentityId == subject && membership.TenantId == tenantId,
            ct).ConfigureAwait(false);
        return memberships.Count > 0;
    }
}
