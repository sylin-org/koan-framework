using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Koan.Data.Core;
using Koan.Tenancy;

namespace Koan.Identity.Tenancy;

/// <summary>
/// SEC-0007 P4 — the request-path tenant scoping that makes multi-tenant isolation real for inbound web requests
/// (the SnapVault D1 gap: only a seam shipped before). Runs at the <c>AfterAuthentication</c> stage: resolves a
/// candidate tenant from the registered carriers (claim → header → subdomain → path, first non-null wins),
/// <b>membership-authorizes</b> it against the authenticated subject (safe-by-default — a forged header/path can
/// never scope a non-member in), <b>projects that membership's roles onto the request principal</b> (so the
/// authorize floor, which reads role claims, actually HONORS <c>Membership.Roles</c> — without this the tenancy role
/// binding would be write-only), then wraps the rest of the pipeline in <c>Tenant.Use(...)</c>. Enforcement is on
/// the request path, re-evaluated every request, so a removed membership stops scoping (and stops conferring its
/// roles) at the very next request. A resolved-but-unauthorized or unresolved request proceeds <i>unscoped</i> — any
/// tenant-scoped read/write fails closed downstream rather than leaking, and we never 403 (no tenant-existence oracle).
/// <para>Note: tenant <i>lifecycle</i> status (<c>TenantStatus.Suspended</c>) is NOT enforced here — that is
/// ARCH-0099 P3 (and read-permissive by design); a member of a suspended tenant is still scoped in for now.</para>
/// <para>App code must read the active tenant from <c>Tenant.Current</c> (the authorized ambient axis) — never
/// re-derive it from the raw path segment or host label, which are unauthenticated client-asserted strings.</para>
/// </summary>
internal sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IEnumerable<ITenantResolver> resolvers, IOptions<TenancyResolutionOptions> options)
    {
        var opts = options.Value;
        var subject = ReadSubject(context.User);

        // Short-circuit: when membership is required, an anonymous request can never be scoped in — skip the carrier
        // resolution (and its control-plane lookups) entirely, so a forged-carrier spray cannot amplify store reads.
        if (opts.RequireMembership && string.IsNullOrEmpty(subject))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var request = new TenantResolutionRequest(
            Host: context.Request.Host.Host,
            Path: context.Request.Path.Value,
            Subject: subject,
            Claim: type => context.User?.FindFirst(type)?.Value,
            Header: name => context.Request.Headers.TryGetValue(name, out var v) ? v.ToString() : null);

        string? candidate = null;
        foreach (var resolver in resolvers)
        {
            candidate = await resolver.ResolveAsync(request, context.RequestAborted).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(candidate)) break;
        }

        if (string.IsNullOrEmpty(candidate))
        {
            await _next(context).ConfigureAwait(false); // no carrier matched — leave the ambient as-is (e.g. a dev fallback)
            return;
        }

        // One query both authorizes the candidate AND yields the roles to project (no second round-trip).
        var memberships = string.IsNullOrEmpty(subject)
            ? (IReadOnlyList<Membership>)Array.Empty<Membership>()
            : await Membership.Query(m => m.IdentityId == subject && m.TenantId == candidate, context.RequestAborted).ConfigureAwait(false);

        if (opts.RequireMembership && memberships.Count == 0)
        {
            await _next(context).ConfigureAwait(false); // resolved but unauthorized — proceed unscoped (fail closed downstream)
            return;
        }

        var original = context.User;
        var augmented = ProjectRoles(original, memberships);
        if (augmented is not null) context.User = augmented;
        try
        {
            using (Tenant.Use(candidate))
                await _next(context).ConfigureAwait(false);
        }
        finally
        {
            if (augmented is not null) context.User = original;
        }
    }

    /// <summary>
    /// Returns a principal carrying the tenant's membership roles as <see cref="ClaimTypes.Role"/> claims (so the
    /// authorize floor honors them), or null when there is nothing new to add. The original principal is never mutated
    /// (a fresh identity is added to a copy), so the projected roles live exactly for the duration of the scope.
    /// </summary>
    internal static ClaimsPrincipal? ProjectRoles(ClaimsPrincipal principal, IReadOnlyList<Membership> memberships)
    {
        var roles = memberships
            .SelectMany(m => m.Roles)
            // A tenant membership must NEVER project a HOST role onto the request principal — otherwise a
            // membership carrying a host operator role (via any write path) would confer global authority through
            // this projection. Enforce both known host planes structurally at this projection chokepoint, not at
            // individual membership write sites (closure over discipline).
            .Where(r => !string.IsNullOrWhiteSpace(r)
                        && !TenancyRoles.IsReservedHostRole(r)
                        && !IdentityRoles.IsReservedHostRole(r)
                        && !principal.IsInRole(r))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (roles.Count == 0) return null;

        var clone = new ClaimsPrincipal(principal.Identities); // copies the identity refs; AddIdentity won't mutate `principal`
        clone.AddIdentity(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r))));
        return clone;
    }

    /// <summary>The canonical subject id (<c>sub</c> → <c>NameIdentifier</c>), matching Koan.Web's AuthSubject / SEC-0001 KoanIdentity.Id.</summary>
    internal static string? ReadSubject(ClaimsPrincipal? principal)
    {
        if (principal is null) return null;
        var sub = principal.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(sub)) return sub;
        var nameId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(nameId) ? null : nameId;
    }

    /// <summary>True when <paramref name="subject"/> holds a <c>Membership</c> in <paramref name="tenantId"/>. Anonymous is never a member. Retained for callers that only need the boolean.</summary>
    internal static async Task<bool> IsMemberAsync(string? subject, string tenantId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(subject)) return false;
        var memberships = await Membership.Query(m => m.IdentityId == subject && m.TenantId == tenantId, ct).ConfigureAwait(false);
        return memberships.Count > 0;
    }
}
