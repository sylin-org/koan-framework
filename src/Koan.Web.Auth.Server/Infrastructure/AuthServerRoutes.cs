namespace Koan.Web.Auth.Server.Infrastructure;

/// <summary>Stable HTTP vocabulary owned by the embedded authorization server.</summary>
internal static class AuthServerRoutes
{
    public const string OAuthBase = "/oauth";
    public const string Authorize = OAuthBase + "/authorize";
    public const string Request = OAuthBase + "/request/{rid}";
    public const string Approve = Request + "/approve";
    public const string Deny = Request + "/deny";
    public const string Token = OAuthBase + "/token";
    public const string Device = OAuthBase + "/device";
    public const string Register = OAuthBase + "/register";
    public const string DevToken = OAuthBase + "/dev-token";

    public const string AuthorizationServerMetadata = "/.well-known/oauth-authorization-server";
    public const string OpenIdConfiguration = "/.well-known/openid-configuration";
    public const string Jwks = "/.well-known/jwks.json";
}
