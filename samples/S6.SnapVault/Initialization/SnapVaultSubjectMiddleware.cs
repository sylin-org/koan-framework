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
using S6.SnapVault.Services;

namespace S6.SnapVault.Initialization;

/// <summary>
/// SnapVault step 5e — the operator+guest subject-resolution middleware. Runs at the <c>AfterAuthentication</c> stage
/// (after <see cref="Koan.Identity.Tenancy"/>'s tenant resolver, Order 100) and sets the ambient SEC-0008
/// <see cref="Subject"/> for the rest of the request, so the fail-closed access axis has a subject to read. Precedence:
/// <list type="number">
/// <item><b>Guest</b> — an authenticated person holding active <see cref="Models.GalleryGrant"/>s is scoped to their
/// granted events (<c>Subject.Use(scopes)</c>) within their studio (<c>Tenant.Use</c>, derived server-side from the
/// grants — never a client hint). A scope-resolution throw fails closed (no subject), NEVER an operator.</item>
/// <item><b>Operator via carrier</b> — the tenant middleware resolved + membership-authorized a studio (the 5f
/// studio-picker), so an authorized member reads unconstrained within it.</item>
/// <item><b>Dev-trust operator</b> — Development + ANONYMOUS only (the SPA has no login yet): an anonymous dev request
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

        // 2. OPERATOR via carrier — the tenant middleware resolved + membership-authorized a studio (5f picker).
        if (Tenant.Current is { Id: { Length: > 0 } tenantId })
        {
            using (Subject.Unconstrained(personId ?? ("operator:" + tenantId)))
                await _next(context);
            return;
        }

        // 3. DEV-TRUST operator fallback — Development + ANONYMOUS only (until the 5f studio-picker carrier).
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
