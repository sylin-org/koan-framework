namespace Koan.Web.Auth.Extensions;

public static class AuthenticationExtensions
{
    /// <summary>
    /// The Koan cookie sign-in scheme. Every per-provider OAuth2/OIDC handler (seeded by
    /// <see cref="Hosting.AuthSchemeSeeder"/>, WEB-0071) sets <c>SignInScheme</c> to this so the cookie's
    /// flow-dispatch events (sign-in / validate-principal / challenge) still run (WEB-0066).
    /// </summary>
    public const string CookieScheme = "Koan.cookie";

    /// <summary>
    /// SEC-0025 — the scheme that authenticates an ordinary request when the application is a resource server
    /// (<c>Koan:Web:Auth:ResourceServer:Enabled</c>): the bearer, for this application's resource, when the request carries
    /// an <c>Authorization</c> header; <see cref="CookieScheme"/> otherwise. It then becomes the default authenticate,
    /// challenge and forbid scheme; sign-in and sign-out stay with the cookie.
    /// </summary>
    public const string ResourceServerScheme = "Koan.resource";
}
