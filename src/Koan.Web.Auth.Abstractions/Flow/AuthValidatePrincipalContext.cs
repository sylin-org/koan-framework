using Microsoft.AspNetCore.Authentication.Cookies;

namespace Koan.Web.Auth.Flow;

/// <summary>
/// Context passed to <see cref="IKoanAuthFlowHandler.OnValidatePrincipal"/>. Thin wrapper around
/// ASP.NET Core's <see cref="CookieValidatePrincipalContext"/> so handlers can call the standard
/// <see cref="CookieValidatePrincipalContext.RejectPrincipal"/> /
/// <see cref="CookieValidatePrincipalContext.ReplacePrincipal"/> APIs while still receiving the
/// per-request <see cref="System.IServiceProvider"/> for any DB lookups they need.
/// </summary>
/// <remarks>
/// <para>
/// All handlers observe the same underlying <see cref="CookieValidatePrincipalContext"/>; the
/// dispatcher does not short-circuit on <see cref="CookieValidatePrincipalContext.RejectPrincipal"/>
/// — multiple handlers can co-exist (e.g. one revoking the principal, another emitting audit).
/// </para>
/// <para>
/// Typical use: re-fetch the user row by <c>NameIdentifier</c>, check
/// <c>DeletedAt is not null</c>, call <see cref="CookieValidatePrincipalContext.RejectPrincipal"/>
/// to force a fresh challenge. Cheap because cookie auth caches the validated principal until the
/// next sliding-expiration tick — call the DB only when the principal hasn't been re-validated
/// recently.
/// </para>
/// </remarks>
public sealed class AuthValidatePrincipalContext
{
    public required CookieValidatePrincipalContext Inner { get; init; }
    public required IServiceProvider Services { get; init; }
}
