using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Koan.Data.Core;
using Koan.Identity.Impersonation;
using Koan.Identity.Management;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;

namespace Koan.Identity.Reconciliation;

/// <summary>
/// SEC-0007 P0/P1 — the wired sign-in trigger. The cookie pipeline (OnSigningIn → AuthFlowDispatcher) drives every
/// discovered <see cref="IKoanAuthFlowHandler"/>; this one runs late (after identity-mapping) and (P0) upserts the
/// durable <see cref="Identity"/> from the baked claims, then (P1) records a device <see cref="Session"/> and
/// stamps its id on the cookie so revocation is enforceable. <c>OnValidatePrincipal</c> rejects a principal whose
/// session was revoked or whose person is no longer active; <c>OnSignOut</c> revokes the session.
/// </summary>
public sealed class IdentityAuthFlowHandler : IKoanAuthFlowHandler
{
    // Reconciliation observes the resolved subject, so it must run after any identity-mapping handler (int.MinValue).
    public int Priority => 1000;

    public async Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        var subject = ctx.UserId;
        if (string.IsNullOrWhiteSpace(subject)) return; // fail-closed: no id → no person (mirrors AuthSchemeSeeder)

        var reconciler = ctx.Services.GetService<IIdentityReconciler>();
        if (reconciler is not null)
        {
            var claims = ctx.Identity;
            var displayName = claims.FindFirst(ClaimTypes.Name)?.Value ?? claims.FindFirst("name")?.Value;
            var picture = claims.FindFirst("avatar")?.Value ?? claims.FindFirst("picture")?.Value;
            var email = claims.FindFirst(ClaimTypes.Email)?.Value ?? claims.FindFirst("email")?.Value;
            var emailVerified = string.Equals(claims.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            var person = await reconciler.ReconcileAsync(
                new IdentityClaims(subject, displayName, picture, email, emailVerified, ctx.Provider), ct).ConfigureAwait(false);

            // Person ≠ email merge: if this IdP sub merged onto an existing person, rewrite the cookie's subject to
            // the canonical person id so downstream (roles, session, Membership) all resolve to ONE identity.
            if (!string.Equals(person.Id, subject, StringComparison.Ordinal))
            {
                if (ctx.Identity.FindFirst(ClaimTypes.NameIdentifier) is { } nameId) ctx.Identity.RemoveClaim(nameId);
                ctx.Identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, person.Id));
                subject = person.Id;
            }
        }

        // SEC-0007 P3-grp4 — pre-session gates (e.g. MFA step-up). Consulted with the CANONICAL subject, BEFORE any
        // role stamping or Session creation: a blocking gate Rejects, which empties the cookie principal — so no
        // authenticated session is written until the required factor is satisfied (the 2-phase sign-in enforcement).
        var principalView = new ClaimsPrincipal(ctx.Identity);
        foreach (var gate in ctx.Services.GetServices<ISignInGate>().OrderBy(g => g.Order))
        {
            SignInGateBlock? block;
            try
            {
                block = await gate.EvaluateAsync(subject, principalView, ctx.Services, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // FAIL CLOSED: a gate that cannot be evaluated (a transient store fault, a ticket-save failure) must
                // ABORT the sign-in, never allow it — otherwise the dispatcher swallows the exception and continues
                // with no rejection, issuing a full cookie that silently bypasses the required factor. "Cannot prove
                // the gate is satisfied ⇒ do not issue a session."
                ctx.Services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Identity.SignInGate")
                    ?.LogWarning(ex, "Sign-in gate {Gate} threw for {Subject}; failing closed (sign-in aborted).", gate.GetType().FullName, subject);
                ctx.Reject("koan_signin_gate_error");
                return;
            }
            if (block is not null) { ctx.Reject(block.Reason); return; }
        }

        // Stamp the person's GLOBAL roles (IdentityRole, Layer 2) onto the cookie so production actually HONORS a
        // global grant — without this the binding would be write-only (the authorize floor reads role claims). This
        // also makes the access explainer's "preview == production" true for global roles.
        foreach (var globalRole in await IdentityRole.Query(r => r.IdentityId == subject, ct).ConfigureAwait(false))
            if (!ctx.Identity.HasClaim(ClaimTypes.Role, globalRole.RoleKey))
                ctx.Identity.AddClaim(new Claim(ClaimTypes.Role, globalRole.RoleKey));

        // Record a durable device session and stamp its id on the cookie so "sign out everywhere-else" can revoke
        // this specific session and OnValidatePrincipal can enforce it.
        var ua = ctx.HttpContext.Request.Headers.UserAgent.ToString();
        var session = new Session { IdentityId = subject, Browser = Truncate(ua, 256) };
        await session.Save(ct).ConfigureAwait(false);
        ctx.Identity.AddClaim(new Claim(SessionGuard.SessionClaim, session.Id));
    }

    public async Task OnValidatePrincipal(AuthValidatePrincipalContext ctx, CancellationToken ct)
    {
        if (ctx.Inner.Principal is not { } principal) return;

        if (await SessionGuard.ShouldRejectAsync(principal, ct).ConfigureAwait(false))
        {
            ctx.Inner.RejectPrincipal();
            return;
        }

        // Impersonation re-check: if acting-as, the grant must still be active — revoke/expiry ends the session on
        // the next request (the impersonated cookie stops working the moment the grant is gone).
        if (ImpersonationClaims.IsImpersonating(principal))
        {
            var actor = ImpersonationClaims.ActorOf(principal);
            var target = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
            var impersonation = ctx.Services.GetService<ImpersonationService>();
            if (actor is not null && target is not null && impersonation is not null
                && !await impersonation.IsActiveAsync(actor, target, ct).ConfigureAwait(false))
                ctx.Inner.RejectPrincipal();
        }
    }

    public async Task OnSignOut(AuthSignOutContext ctx, CancellationToken ct)
    {
        var sid = ctx.HttpContext.User?.FindFirst(SessionGuard.SessionClaim)?.Value;
        if (string.IsNullOrEmpty(sid)) return;
        var sessions = ctx.Services.GetService<SessionService>();
        if (sessions is not null) await sessions.RevokeAsync(sid, ct).ConfigureAwait(false);
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? null : (value.Length > max ? value[..max] : value);
}
