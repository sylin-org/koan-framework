using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Koan.Data.Access;
using Koan.Tenancy;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SnapVault.Services;

namespace SnapVault.Initialization;

/// <summary>
/// Resolves the operator or guest subject after authentication and tenant resolution, then sets the ambient
/// <see cref="Subject"/> for the rest of the request, so the fail-closed access axis has a subject to read. Precedence:
/// <list type="number">
/// <item><b>Guest</b> — an authenticated person holding active <see cref="Models.GalleryGrant"/>s is scoped to their
/// granted events (<c>Subject.Use(scopes)</c>) within their studio (<c>Tenant.Use</c>, derived server-side from the
/// grants — never a client hint). A scope-resolution throw fails closed (no subject), NEVER an operator.</item>
/// <item><b>Operator via carrier</b> — the tenant middleware resolved and membership-authorized a studio, so an
/// authorized member reads unconstrained within it.</item>
/// <item><b>Dev-trust operator</b> — Development and anonymous only: an anonymous local request
/// reads unconstrained in its default tenant. Restricted to anonymous so an authenticated-but-grantless principal (a
/// revoked client) still fails closed. Production has NO fallback.</item>
/// <item>otherwise — no ambient subject ⇒ fail-closed downstream.</item>
/// </list>
/// </summary>
public sealed class SnapVaultSubjectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly GuestScopeService _guestScopes;
    private readonly IHostEnvironment _env;
    private readonly ILogger<SnapVaultSubjectMiddleware> _logger;

    public SnapVaultSubjectMiddleware(
        RequestDelegate next, GuestScopeService guestScopes, IHostEnvironment env, ILogger<SnapVaultSubjectMiddleware> logger)
    {
        _next = next;
        _guestScopes = guestScopes;
        _env = env;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var personId = ReadSubject(context.User);

        // 1. GUEST — an authenticated person holding active grants, scoped to their granted events in their studio.
        if (!string.IsNullOrEmpty(personId))
        {
            GuestAccess? guest;
            try
            {
                guest = await _guestScopes.ResolveGuestAsync(personId!, context.RequestAborted);
            }
            catch (Exception ex)
            {
                // Fail-closed on a scope-resolution throw: proceed with NO subject (deny downstream); a resolution
                // error must NEVER widen access by falling through as an operator.
                _logger.LogWarning(ex, "Guest scope resolution failed for {PersonId}; proceeding fail-closed", personId);
                await _next(context);
                return;
            }

            if (guest is not null)
            {
                using (Tenant.Use(guest.StudioTenantId))
                using (Subject.Use(personId!, guest.Scopes))
                    await _next(context);
                return;
            }
            // Authenticated but no grants ⇒ NOT a guest, and the dev-trust operator is ANONYMOUS-only ⇒ fall to the
            // carrier check; a revoked client (grants gone) with no carrier therefore fails closed below.
        }

        // Operator via carrier: tenant resolution already authorized this studio membership.
        if (Tenant.Current is { Id: { Length: > 0 } tenantId })
        {
            using (Subject.Unconstrained(personId ?? ("operator:" + tenantId)))
                await _next(context);
            return;
        }

        // Development-only anonymous operator fallback.
        if (_env.IsDevelopment() && string.IsNullOrEmpty(personId))
        {
            using (Subject.Unconstrained("dev-operator"))
                await _next(context);
            return;
        }

        // 4. No guest, no carrier, not a dev anonymous operator ⇒ no ambient subject ⇒ fail-closed downstream.
        await _next(context);
    }

    /// <summary>The canonical person id (<c>sub</c> → <c>NameIdentifier</c>); null for an unauthenticated request.</summary>
    private static string? ReadSubject(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true) return null;
        var sub = principal.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(sub)) return sub;
        var nameId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(nameId) ? null : nameId;
    }
}

/// <summary>Mounts <see cref="SnapVaultSubjectMiddleware"/> after the framework tenant resolver (Order 100).</summary>
public sealed class SnapVaultSubjectContributor : IKoanWebPipelineContributor
{
    public KoanWebPipelineStage Stage => KoanWebPipelineStage.AfterAuthentication;
    public int Order => 200;
    public void Configure(IApplicationBuilder app) => app.UseMiddleware<SnapVaultSubjectMiddleware>();
}
