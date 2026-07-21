namespace Koan.Web.Auth.Connector.Test.Infrastructure;

internal static class Constants
{
    public static class Routes
    {
        public const string Base = "/.testoauth";
        public const string Login = Base + "/login.html";
        public const string Authorize = Base + "/authorize";
        public const string Token = Base + "/token";
        public const string UserInfo = Base + "/userinfo";
        public const string Discovery = Base + "/.well-known/openid-configuration";
        public const string Jwks = Base + "/jwks";
    }

    // Dev persona cookie used by the TestProvider login UI
    public const string CookieUser = "_tp_user";

    // Prefix for custom claim entries passed via query string
    public const string ClaimPrefix = "claim.";
}
