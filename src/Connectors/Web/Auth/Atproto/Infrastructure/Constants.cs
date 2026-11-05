namespace Koan.Web.Auth.Connector.Atproto.Infrastructure;

internal static class Constants
{
    internal const string Provider = "atproto";
    internal const string Protocol = "atproto";
    internal const string Section = "Koan:Web:Auth:Atproto";
    internal const string ProviderSection = "Koan:Web:Auth:Providers:atproto";
    internal const string Callback = "/auth/atproto/callback";
    internal const string Metadata = "/auth/atproto/client-metadata.json";
    internal const string RequestedScopes = ".atproto.requested_scopes";
    internal const string Account = ".atproto.account";
    internal const string SessionProtectionPurpose = "Koan.Web.Auth.Atproto.Session.v1";
    internal const string StateProtectionPurpose = "Koan.Web.Auth.Atproto.State.v1";
    internal const string PropertiesPurpose = "Koan.Web.Auth.Atproto.Browser.v1";
    internal const string BaseScope = "atproto";
    internal const string PdsServiceType = "AtprotoPersonalDataServer";

    /// <summary>How long a pending authorization is accepted after its challenge.</summary>
    internal static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The longest a refresh token lives for a public client under the AT Protocol OAuth profile
    /// (https://atproto.com/specs/oauth): a session not refreshed for this long can no longer be, so its row is pruned.
    /// </summary>
    internal static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(14);

    /// <summary>How long before its access token expires a session is refreshed; the SDK's provider uses the same.</summary>
    internal static readonly TimeSpan RefreshAhead = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The most provider requests a holder makes under a claim, each within the request timeout: a refresh or a
    /// revocation, and its nonce retry. Everything else a revocation needs is read before the claim is taken.
    /// </summary>
    internal const int ClaimedRequests = 2;

    /// <summary>The longest the write that settles a claim waits on the store.</summary>
    internal static readonly TimeSpan ClaimWriteBound = TimeSpan.FromSeconds(10);

    /// <summary>The pause between reads of a session another process holds.</summary>
    internal static readonly TimeSpan ClaimPause = TimeSpan.FromMilliseconds(250);

    /// <summary>How many times a sign-in reads the session again when it changed between the read and the commit.</summary>
    internal const int CommitAttempts = 5;

    /// <summary>How often expired pending authorizations and sessions are swept, and how many rows each read takes.</summary>
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);
    internal const int SweepPageSize = 100;

    /// <summary>The longest service-auth token read.</summary>
    internal const int ServiceAuthLongestToken = 8192;

    /// <summary>The longest life a service-auth token may claim.</summary>
    internal static readonly TimeSpan ServiceAuthLongestLife = TimeSpan.FromMinutes(5);

    /// <summary>How far ahead of this clock a service-auth token may say it was issued.</summary>
    internal static readonly TimeSpan ServiceAuthIssuedAhead = TimeSpan.FromSeconds(30);
}
