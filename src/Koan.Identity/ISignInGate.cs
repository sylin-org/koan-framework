using System.Security.Claims;
using Koan.Core;

namespace Koan.Identity;

/// <summary>
/// SEC-0007 P3-grp4 — a <b>pre-session gate</b> consulted by the sign-in pipeline AFTER the canonical person is
/// resolved but BEFORE the durable <see cref="Session"/> is issued. A gate that returns a block short-circuits the
/// sign-in: the cookie pipeline replaces the principal with an empty identity on <c>Reject</c>, so <b>no
/// authenticated session is written and no <see cref="Session"/> row is created</b> — a required-but-unsatisfied
/// factor (e.g. MFA at login) therefore cannot yield a full session. This is the request-path enforcement that makes
/// the framework-native 2-phase (interrupted → resume) sign-in real rather than a write-only flag.
/// <para>Discovered (<c>[KoanDiscoverable]</c>) — the <c>Koan.Identity.Credentials</c> base ships the step-up gate;
/// with no Credentials package referenced there are no gates and sign-in is unchanged (graceful degradation).</para>
/// </summary>
[KoanDiscoverable]
public interface ISignInGate
{
    /// <summary>Lower runs first; the first block wins.</summary>
    int Order => 0;

    /// <summary>
    /// Evaluate the in-progress sign-in for <paramref name="identityId"/> (the canonical person id) given the
    /// <paramref name="principal"/> being baked into the cookie (its <c>amr</c> claims report the factors already
    /// satisfied this round). Return a <see cref="SignInGateBlock"/> to abort, or null to allow.
    /// </summary>
    Task<SignInGateBlock?> EvaluateAsync(string identityId, ClaimsPrincipal principal, IServiceProvider services, CancellationToken ct = default);
}

/// <summary>
/// A gate's decision to block a sign-in. <see cref="Reason"/> is surfaced verbatim via the
/// <c>Koan.Web.Auth.SignInRejected</c> marker so a controller can translate it into a redirect (it may carry a
/// resume token, e.g. <c>koan_stepup:&lt;ticketId&gt;</c>).
/// </summary>
public sealed record SignInGateBlock(string Reason);
