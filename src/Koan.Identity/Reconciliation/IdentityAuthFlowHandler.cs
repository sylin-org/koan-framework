using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;

namespace Koan.Identity.Reconciliation;

/// <summary>
/// SEC-0007 P0 — the wired reconciliation trigger. The cookie sign-in pipeline (OnSigningIn → AuthFlowDispatcher)
/// drives every discovered <see cref="IKoanAuthFlowHandler"/>; this one runs late (after identity-mapping has
/// resolved the platform user id) and upserts the durable <see cref="Identity"/> from the baked claims. Universal:
/// it fires for OAuth/OIDC, the dev provider, and programmatic <c>SignInAsync</c> alike.
/// </summary>
public sealed class IdentityAuthFlowHandler : IKoanAuthFlowHandler
{
    // Reconciliation observes the resolved subject, so it must run after any identity-mapping handler (int.MinValue).
    // A high priority keeps it late in the pipeline.
    public int Priority => 1000;

    public async Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        var subject = ctx.UserId;
        if (string.IsNullOrWhiteSpace(subject)) return; // fail-closed: no id → no person (mirrors AuthSchemeSeeder)

        var reconciler = ctx.Services.GetService<IIdentityReconciler>();
        if (reconciler is null) return;

        var claims = ctx.Identity;
        var displayName = claims.FindFirst(ClaimTypes.Name)?.Value ?? claims.FindFirst("name")?.Value;
        var picture = claims.FindFirst("avatar")?.Value ?? claims.FindFirst("picture")?.Value;
        var email = claims.FindFirst(ClaimTypes.Email)?.Value ?? claims.FindFirst("email")?.Value;
        var emailVerified = string.Equals(claims.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);

        await reconciler.ReconcileAsync(
            new IdentityClaims(subject, displayName, picture, email, emailVerified, ctx.Provider),
            ct).ConfigureAwait(false);
    }
}
