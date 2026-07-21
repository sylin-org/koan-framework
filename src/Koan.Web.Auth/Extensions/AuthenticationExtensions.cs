namespace Koan.Web.Auth.Extensions;

public static class AuthenticationExtensions
{
    /// <summary>
    /// The Koan cookie sign-in scheme. Every per-provider OAuth2/OIDC handler (seeded by
    /// <see cref="Hosting.AuthSchemeSeeder"/>, WEB-0071) sets <c>SignInScheme</c> to this so the cookie's
    /// flow-dispatch events (sign-in / validate-principal / challenge) still run (WEB-0066).
    /// </summary>
    public const string CookieScheme = "Koan.cookie";
}
