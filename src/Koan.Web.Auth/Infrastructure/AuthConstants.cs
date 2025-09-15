namespace Koan.Web.Auth.Infrastructure;

public static class AuthConstants
{
    public static class Configuration
    {
        public const string Section = "Koan:Web:Auth";
        public const string AllowDynamicProvidersInProduction = "Koan:Web:Auth:AllowDynamicProvidersInProduction";
    }

    public static class Routes
    {
        public const string Discovery = "/.well-known/auth/providers";
        public const string AuthBase = "/auth";
        public const string Challenge = "/auth/{provider}/challenge";
        public const string Callback = "/auth/{provider}/callback";
        public const string Logout = "/auth/logout";
        public const string SamlMetadata = "/auth/{provider}/saml/metadata";
        public const string SamlAcs = "/auth/{provider}/saml/acs";
        public const string Me = "/me";
        public const string Connections = "/me/connections";
        public const string ConnectionsProviders = "/me/connections/providers";
        public const string ConnectionsLink = "/me/connections/{provider}/link";
        public const string ConnectionsUnlink = "/me/connections/{provider}/{keyHash}";
    }

    public static class Protocols
    {
        public const string Oidc = "oidc";
        public const string OAuth2 = "oauth2";
        public const string Saml = "saml";
    }

    // Development helpers used by first-party samples/providers
    public static class Dev
    {
        // Name of the lightweight persona cookie used by the TestProvider during development
        public const string TestProviderCookieUser = "_tp_user";
    }
}
