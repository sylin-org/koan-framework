using Microsoft.AspNetCore.Authentication.Cookies;
using Koan.Core;
using Koan.Web.Auth.Contributors;

namespace Koan.Web.Auth.Flow;

/// <summary>
/// Single discoverable plug-in surface for the cookie-auth lifecycle. Auto-discovered via
/// <see cref="KoanDiscoverableAttribute"/> (registry-backed; no DI registration required); registered as a
/// scoped service. Each event runs the full pipeline of handlers once in <see cref="Priority"/> order, sequentially.
/// </summary>
/// <remarks>
/// <para>
/// Broadens the surface of the older <see cref="IKoanAuthEventContributor"/>. Existing
/// implementations of the old interface continue to work — the framework registers a
/// <c>LegacyAuthContributorAdapter</c> that re-projects each contributor as a flow handler so the
/// dispatcher has a single contract to drive. New code should implement
/// <see cref="IKoanAuthFlowHandler"/> directly.
/// </para>
/// <para>
/// Default-implemented members let handlers override only the events they care about. The full
/// set covers:
/// </para>
/// <list type="bullet">
///   <item>Startup reconciliation (<see cref="OnBootstrap"/>)</item>
///   <item>Per-request principal validation (<see cref="OnValidatePrincipal"/>)</item>
///   <item>Sign-in / sign-out (<see cref="OnSignIn"/>, <see cref="OnSignOut"/>)</item>
///   <item>Challenge / forbidden response shaping (<see cref="OnChallenge"/>, <see cref="OnAccessDenied"/>)</item>
/// </list>
/// <para>
/// <b>Failure semantics:</b> per-handler exceptions are logged and swallowed by the dispatcher;
/// the next handler still runs. <see cref="System.OperationCanceledException"/> propagates so
/// host-shutdown and client-abort terminate cleanly.
/// </para>
/// <para>
/// <b>Short-circuit semantics</b> differ per event:
/// </para>
/// <list type="bullet">
///   <item><see cref="OnSignIn"/>: a handler may call <see cref="AuthSignInContext.Reject(string)"/> to
///     stop the pipeline (existing contract).</item>
///   <item><see cref="OnChallenge"/> / <see cref="OnAccessDenied"/>: a handler may set
///     <see cref="AuthChallengeContext.ResponseHandled"/> (or <see cref="AuthAccessDeniedContext.ResponseHandled"/>)
///     to skip the default redirect emission. Subsequent handlers still run for side effects,
///     but their writes to the response are no-ops since the response has already been shaped.</item>
///   <item><see cref="OnValidatePrincipal"/>: handlers call
///     <see cref="Microsoft.AspNetCore.Authentication.Cookies.CookieValidatePrincipalContext.RejectPrincipal"/>
///     on the underlying cookie context to force a fresh challenge; the dispatcher does not
///     short-circuit on that signal — every handler observes the same context.</item>
/// </list>
/// </remarks>
[KoanDiscoverable]
public interface IKoanAuthFlowHandler
{
    /// <summary>Lower values run first within a single event invocation. Default 0. Identity-mapping uses <see cref="int.MinValue"/>.</summary>
    int Priority => 0;

    /// <summary>
    /// One-time startup work via the framework's bootstrap hosted service. Use for set-wide
    /// reconciliation: backfilling roles, seeding initial entities, validating external sources.
    /// Reuses the existing <see cref="AuthBootstrapContext"/> shape.
    /// </summary>
    Task OnBootstrap(AuthBootstrapContext ctx, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Runs on every authenticated request as the cookie middleware re-validates the principal.
    /// The wrapped <see cref="CookieValidatePrincipalContext"/> is mutable: a handler can call
    /// <see cref="CookieValidatePrincipalContext.RejectPrincipal"/> to force the user back through
    /// the challenge, or stamp dynamic claims onto the principal. This is the right hook for
    /// "is this session still valid?" checks (revocation, user-soft-deletion).
    /// </summary>
    Task OnValidatePrincipal(AuthValidatePrincipalContext ctx, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Runs during sign-in, AFTER identity-mapping handlers have resolved the platform user id.
    /// Mutate <see cref="AuthSignInContext.Identity"/> to bake claims into the cookie. Call
    /// <see cref="AuthSignInContext.Reject(string)"/> to short-circuit the pipeline.
    /// </summary>
    Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Runs during sign-out. Use for cleanup, audit emission, per-user cache invalidation. The
    /// user id may be <see langword="null"/> if the outgoing cookie did not carry one.
    /// </summary>
    Task OnSignOut(AuthSignOutContext ctx, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Runs when an unauthenticated request hits a <c>[Authorize]</c>-gated endpoint and cookie
    /// auth is about to issue its sign-in redirect. Handlers can replace the redirect target, set
    /// the response status directly (e.g. 401 for XHR/JSON), or do both. Marking
    /// <see cref="AuthChallengeContext.ResponseHandled"/> suppresses the framework's default
    /// redirect emission.
    /// </summary>
    Task OnChallenge(AuthChallengeContext ctx, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Runs when an authenticated-but-not-authorized request is about to be redirected to the
    /// access-denied page. Same shape as <see cref="OnChallenge"/>: rewrite the URL, set a status
    /// code, or mark the response handled.
    /// </summary>
    Task OnAccessDenied(AuthAccessDeniedContext ctx, CancellationToken ct)
        => Task.CompletedTask;
}
