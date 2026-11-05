namespace Koan.Web.Auth.Infrastructure;

public static class AuthConstants
{
    public static class Routes
    {
        public const string Discovery = "/.well-known/auth/providers";
        public const string AuthBase = "/auth";
        public const string Challenge = "/auth/{provider}/challenge";
        public const string Callback = "/auth/{provider}/callback";
        public const string Logout = "/auth/logout";
        public const string Me = "/me";
        public const string Connections = "/me/connections";
        public const string ConnectionsProviders = "/me/connections/providers";
        public const string ConnectionsLink = "/me/connections/{provider}/link";
        public const string ConnectionsUnlink = "/me/connections/{provider}/{keyHash}";
    }

    /// <summary>The query keys of the challenge and of the failure landing (WEB-0080).</summary>
    public static class Query
    {
        /// <summary>Where a completed sign-in returns to.</summary>
        public const string Return = "return";
        /// <summary>Where a failed sign-in lands: a rooted path of this application.</summary>
        public const string Failure = "failure";
        /// <summary>The key the landing receives the sanitized failure code in.</summary>
        public const string Error = "error";
    }

    /// <summary>Keys Koan carries in <c>AuthenticationProperties.Items</c> across a sign-in.</summary>
    public static class Items
    {
        /// <summary>The failure landing named on the challenge (WEB-0080).</summary>
        public const string FailurePath = ".koan.failure";

        /// <summary>The person a binding challenge binds the arriving login to (SEC-0040).</summary>
        public const string Bind = ".koan.bind";

        /// <summary>
        /// The provider whose verified identity a sign-in made outside that provider's callback carries, such as a proof
        /// exchanged for a session. Only trusted server code sets it.
        /// </summary>
        public const string Provider = ".koan.provider";

        /// <summary>When the cookie's person signed in, in Unix seconds; a sliding renewal keeps it.</summary>
        public const string SignedIn = ".koan.signed_in";
    }

    // Development helpers used by first-party samples/providers
    public static class Dev
    {
        // Name of the lightweight persona cookie used by the TestProvider during development
        public const string TestProviderCookieUser = "_tp_user";
    }
}
