using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using CarpaNet.Identity;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using Koan.Core;
using Koan.Web.Auth.Connector.Atproto.Infrastructure;
using Koan.Web.Auth.Connector.Atproto.Options;
using Koan.Web.Auth.Connector.Atproto.Storage;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Domain;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>Native, guarded PDS requests from an authorized server-side session. Never returns credentials.</summary>
public sealed class AtprotoSessions
{
    private readonly AtprotoSessionStore store;
    private readonly AtprotoHttp network;
    private readonly AtprotoOptions options;
    private readonly IOptions<AuthOptions> auth;
    private readonly IAuthProviderCatalog catalog;
    private readonly KoanWebBaseUrl baseUrl;
    private readonly IHttpContextAccessor? http;
    private readonly bool development;
    private readonly ClientCache<AtprotoClient> clients;
    private readonly AtprotoDns dns = new();

    internal AtprotoSessions(AtprotoSessionStore store, AtprotoHttp network, IOptions<AtprotoOptions> options,
        IOptions<AuthOptions> auth, IAuthProviderCatalog catalog, IHostEnvironment environment, KoanWebBaseUrl baseUrl,
        IHttpContextAccessor? http = null)
    {
        this.store = store; this.network = network; this.options = options.Value; this.auth = auth; this.catalog = catalog; this.baseUrl = baseUrl; this.http = http;
        development = KoanEnv.Gate.DevelopmentOnly(environment);
        clients = new ClientCache<AtprotoClient>(this.options.CachedClients);
    }

    /// <summary>
    /// The configured client-metadata URL, else the one derived at use from the application's base URL (WEB-0078): the
    /// configured or accepted one (WEB-0081), else the current request's where Koan derives it. Read at use, so a URL
    /// the application accepts changes the identity from the next request on.
    /// </summary>
    internal string ClientId => PublishedClientId
        ?? throw new AtprotoClientIdentityException(AtprotoClientIdentity.Correction(BaseUrl(), baseUrl.CanAccept && !baseUrl.IsConfigured));

    /// <summary>The client identity this application publishes now, or null when its base URL names no client: a step that
    /// only asks about a stored session then finds it is not current, as it finds one granted to a previous identity.</summary>
    private string? PublishedClientId
        => auth.Value.Providers.GetValueOrDefault(Constants.Provider)?.ClientId
           ?? AtprotoClientIdentity.Derive(BaseUrl(), AllowedScopes, development);

    private string? BaseUrl()
        => baseUrl.Current ?? (baseUrl.DerivesFromRequests && http?.HttpContext is { } context ? baseUrl.Resolve(context) : null);
    internal string[] AllowedScopes => catalog.Find(Constants.Provider)?.Scopes.ToArray() ?? [Constants.BaseScope];
    internal string RedirectUri => AtprotoClientIdentity.RedirectUri(ClientId);
    internal bool Enabled => catalog.Find(Constants.Provider)?.Eligible == true;

    /// <summary>How many signed-in clients this process holds ready now.</summary>
    internal int ClientsHeld => clients.Count;

    /// <summary>Validated, non-secret information about a durable AT session.</summary>
    public sealed record SessionMetadata(string Did, string Pds, string Issuer, IReadOnlyList<string> Scopes);

    private IdentityResolver Resolver(string identifier) => new(network.Client, options.DirectoryFor(identifier, development), dnsResolver: dns);

    /// <summary>Resolve a DID document through guarded protocol transport and require the document's exact identifier.</summary>
    public async Task<DidDocument> ResolveDid(string did, CancellationToken ct = default)
    {
        if (!IdentityResolver.IsValidDid(did)) throw new ArgumentException("A valid DID is required.", nameof(did));
        using var resolver = Resolver(did);
        var document = await resolver.ResolveAsync(did, ct);
        if (document.Id != did) throw new InvalidOperationException("Resolved AT DID document identifier mismatch.");
        return document;
    }
    /// <summary>Who a verified AT identity signs in as, whichever way it was verified.</summary>
    internal static ClaimsPrincipal Principal(string did, string? handle)
    {
        var claims = new ClaimsIdentity(Constants.Provider);
        claims.AddClaim(new Claim(ClaimTypes.NameIdentifier, did));
        claims.AddClaim(new Claim("sub", did));
        claims.AddClaim(new Claim(AtprotoClaimTypes.Did, did));
        if (handle is not null) { claims.AddClaim(new Claim(AtprotoClaimTypes.Handle, handle)); claims.AddClaim(new Claim(ClaimTypes.Name, handle)); }
        return new ClaimsPrincipal(claims);
    }

    /// <summary>The external identity a verified AT identity links, whichever way it was verified.</summary>
    internal static ExternalIdentity External(string did, string? handle) => new()
    {
        UserId = did, Provider = Constants.Provider,
        ProviderKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(did))),
        ClaimsJson = JsonSerializer.Serialize(new { did, handle }),
    };

    /// <summary>Whether the transport reaches <paramref name="host"/> on <paramref name="port"/>, which only an exact
    /// development origin does.</summary>
    internal bool AdmitsPort(string host, string port)
        => network.DevelopmentOrigin(new Uri($"https://{host}:{port}")) || network.DevelopmentOrigin(new Uri($"http://{host}:{port}"));

    /// <summary>The handle <paramref name="document"/> names, when that handle resolves back to its DID. An identity does not
    /// depend on it: a handle that does not resolve is simply no handle.</summary>
    internal async Task<string?> VerifiedHandle(DidDocument document, CancellationToken ct)
    {
        if (document.Handle is not { } handle) return null;
        if (development && options.DevelopmentHandles.TryGetValue(handle, out var mapped)) return mapped == document.Id ? handle : null;
        try
        {
            using var resolver = Resolver(document.Id);
            return (await resolver.ResolveAsync(handle, ct)).Id == document.Id ? handle : null;
        }
        catch (Exception error) when (error is not OperationCanceledException) { return null; }
    }

    // The SDK reads and consumes pending authorizations through the store, and writes sessions only to a capture: the
    // connector stores a grant itself, conditionally.
    private OAuthSession OAuth(string clientId, string scope, string did, SessionCapture sessions) => new(new OAuthClientConfig
    { ClientId = clientId, RedirectUri = AtprotoClientIdentity.RedirectUri(clientId), Scope = scope, HttpClient = network.Client,
        StateStore = store, SessionStore = sessions, IdentityResolver = Resolver(did),
        RefreshBuffer = Constants.RefreshAhead, StateExpiration = Constants.AuthorizationLifetime });

    private async Task<(string Did, string? Handle, string Pds, string Issuer)> Resolve(string identifier, CancellationToken ct)
    {
        using var resolver = Resolver(identifier);
        var input = identifier;
        if (development && options.DevelopmentHandles.TryGetValue(identifier, out var mapped)) input = mapped;
        var document = await Classified(SignInFailure.HandleUnresolved, "AT identity could not be resolved.", () => resolver.ResolveAsync(input, ct));
        if (input.StartsWith("did:", StringComparison.Ordinal) && input != document.Id)
            throw new AtprotoSignInException(SignInFailure.HandleUnresolved, "Resolved AT identity is inconsistent.");
        if (!identifier.StartsWith("did:", StringComparison.Ordinal) && !document.AlsoKnownAs.Any(x => x.Equals("at://" + identifier, StringComparison.OrdinalIgnoreCase)))
            throw new AtprotoSignInException(SignInFailure.HandleUnresolved, "AT handle does not reverse-bind to the resolved DID.");
        var pds = document.PdsEndpoint ?? throw new AtprotoSignInException(SignInFailure.HandleUnresolved, "AT identity has no PDS service.");
        network.Validate(new Uri(pds));
        using var discovery = new AuthorizationServerDiscovery(network.Client, TimeSpan.Zero);
        var issuer = await Classified(SignInFailure.ProviderUnreachable, "AT authorization server could not be discovered.", () => discovery.DiscoverAuthorizationServerAsync(pds, ct));
        network.Validate(new Uri(issuer));
        var metadata = await Classified(SignInFailure.ProviderUnreachable, "AT authorization server metadata could not be read.", () => discovery.GetMetadataAsync(issuer, ct));
        if (metadata.Issuer != issuer || string.IsNullOrWhiteSpace(metadata.PushedAuthorizationRequestEndpoint))
            throw new AtprotoSignInException(SignInFailure.ProviderUnreachable, "AT authorization server metadata is inconsistent or lacks required PAR.");
        foreach (var endpoint in new[] { metadata.AuthorizationEndpoint, metadata.TokenEndpoint, metadata.PushedAuthorizationRequestEndpoint }) network.Validate(new Uri(endpoint!));
        return (document.Id, await VerifiedHandle(document, ct), pds.TrimEnd('/'), issuer);
    }

    internal async Task<string> Start(string identifier, AuthenticationProperties properties, string protectedProperties, CancellationToken ct)
    {
        if (!Enabled) throw new InvalidOperationException("AT provider is inactive.");
        var clientId = ClientId;
        var explicitlyRequested = properties.Items.TryGetValue(Constants.RequestedScopes, out var requested);
        var scopes = explicitlyRequested
            ? JsonSerializer.Deserialize<string[]>(requested!)! : [Constants.BaseScope];
        scopes = ScopesForSignIn(explicitlyRequested, scopes, null, AllowedScopes);
        var identity = await Resolve(identifier, ct);
        if (!explicitlyRequested && (await store.Read(identity.Did, ct))?.Grant is { } saved && Current(saved, clientId) && HasBroaderScopes(saved.TokenSet.Scope))
        {
            ValidateSaved(saved, identity, clientId);
            scopes = ScopesForSignIn(false, scopes, saved.TokenSet.Scope, AllowedScopes);
        }
        var scope = string.Join(' ', scopes.Distinct(StringComparer.Ordinal));
        var binding = new Binding(identity.Did, identity.Pds, identity.Issuer, scope, protectedProperties);
        using var oauth = OAuth(clientId, scope, identity.Did, new SessionCapture());
        var authorization = await Classified(SignInFailure.ProviderUnreachable, "AT authorization request could not be pushed.",
            () => oauth.AuthorizeAsync(identity.Did, JsonSerializer.Serialize(binding), ct));
        RequirePar(authorization, clientId);
        return authorization;
    }

    /// <summary>
    /// Run one sign-in step and classify anything it throws as <paramref name="failure"/> (WEB-0080), keeping the
    /// cause server-side. A step that already classified itself passes through.
    /// </summary>
    private static async Task<T> Classified<T>(SignInFailure failure, string message, Func<Task<T>> step)
    {
        try { return await step(); }
        catch (Exception error) when (error is not OperationCanceledException and not AtprotoSignInException)
        { throw new AtprotoSignInException(failure, message, error); }
    }

    internal static void RequirePar(string authorization, string clientId)
    {
        var query = HttpUtility.ParseQueryString(new Uri(authorization).Query);
        if (query.Count != 2 || query.GetValues("client_id")?.Length != 1 || query["client_id"] != clientId ||
            query.GetValues("request_uri")?.Length != 1 || string.IsNullOrWhiteSpace(query["request_uri"]))
            throw new InvalidOperationException("AT authorization requires successful PAR; direct authorization fallback is rejected.");
    }

    // CarpaNet alpha.5 adds a login_hint for URL input. Initiate server-first PAR
    // explicitly so account selection belongs to the provider; reuse its PKCE/DPoP and callback.
    internal async Task<string> StartServer(string server, AuthenticationProperties properties, string protectedProperties, CancellationToken ct)
    {
        if (!Enabled) throw new InvalidOperationException("AT provider is inactive.");
        // WEB-0081: one client identity for the whole challenge — the pushed request, its redirect_uri and the
        // authorization URL — even if the application accepts another URL while the request is pushed.
        var clientId = ClientId;
        var redirectUri = AtprotoClientIdentity.RedirectUri(clientId);
        network.Validate(new Uri(server));
        using var discovery = new AuthorizationServerDiscovery(network.Client, TimeSpan.Zero);
        var issuer = await Classified(SignInFailure.ProviderUnreachable, "AT authorization server could not be discovered.", async () =>
        {
            try { return await discovery.DiscoverAuthorizationServerAsync(server, ct); }
            catch (OAuthException error) when (error.ErrorCode == "resource_metadata_fetch_failed")
            {
                // An entryway such as bsky.social is an authorization server, not a PDS.
                // Server-first discovery also permits its own AS metadata when no resource metadata exists.
                return server;
            }
        });
        var metadata = await Classified(SignInFailure.ProviderUnreachable, "AT authorization server metadata could not be read.", () => discovery.GetMetadataAsync(issuer, ct));
        if (metadata.Issuer != issuer || string.IsNullOrWhiteSpace(metadata.PushedAuthorizationRequestEndpoint))
            throw new AtprotoSignInException(SignInFailure.ProviderUnreachable, "AT authorization server metadata is inconsistent or lacks required PAR.");
        foreach (var endpoint in new[] { issuer, metadata.AuthorizationEndpoint, metadata.TokenEndpoint, metadata.PushedAuthorizationRequestEndpoint }) network.Validate(new Uri(endpoint!));
        var explicitScopes = properties.Items.TryGetValue(Constants.RequestedScopes, out var requested);
        var scopes = ScopesForSignIn(explicitScopes, explicitScopes ? JsonSerializer.Deserialize<string[]>(requested!)! : [Constants.BaseScope], null, AllowedScopes);
        var scope = string.Join(' ', scopes);
        var (verifier, challenge) = Pkce.Generate();
        var state = Pkce.GenerateState();
        using var key = await DPoPKeyPair.GenerateAsync();
        await store.StoreAsync(state, new OAuthStateData
        {
            Issuer = issuer, PdsUrl = server, DPoPKey = key.ExportKeyPair(), Verifier = verifier,
            AppState = JsonSerializer.Serialize(new Binding(null, null, issuer, scope, protectedProperties)),
            ExpiresAt = DateTimeOffset.UtcNow + Constants.AuthorizationLifetime
        }, ct);
        try
        {
            string? nonce = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, metadata.PushedAuthorizationRequestEndpoint);
                request.Headers.Add("DPoP", await key.CreateProofAsync("POST", metadata.PushedAuthorizationRequestEndpoint, nonce));
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId, ["redirect_uri"] = redirectUri, ["response_type"] = "code",
                    ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256",
                    ["scope"] = scope, ["dpop_jkt"] = key.Thumbprint
                });
                using var response = await Classified(SignInFailure.ProviderUnreachable, "AT authorization request could not be pushed.", () => network.Fetch(request, ct));
                if (response.Headers.TryGetValues("DPoP-Nonce", out var values)) nonce = values.FirstOrDefault();
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (response.IsSuccessStatusCode && body.RootElement.TryGetProperty("request_uri", out var requestUri) && !string.IsNullOrWhiteSpace(requestUri.GetString()))
                {
                    var authorization = metadata.AuthorizationEndpoint + "?client_id=" + Uri.EscapeDataString(clientId) + "&request_uri=" + Uri.EscapeDataString(requestUri.GetString()!);
                    RequirePar(authorization, clientId);
                    return authorization;
                }
                if (attempt == 0 && nonce is not null && body.RootElement.TryGetProperty("error", out var error) && error.GetString() == "use_dpop_nonce") continue;
                throw new InvalidOperationException("AT authorization request failed.");
            }
            throw new InvalidOperationException("AT authorization nonce negotiation failed.");
        }
        catch { await store.ConsumeAsync(state, CancellationToken.None); throw; }
    }

    internal static void RequireSubjectBinding(string? expectedDid, string? expectedPds, string expectedIssuer,
        string actualDid, string actualPds, string actualIssuer, string clientPds)
    {
        if ((expectedDid is not null && expectedDid != actualDid) || (expectedPds is not null && expectedPds != actualPds) ||
            expectedIssuer != actualIssuer || clientPds.TrimEnd('/') != actualPds)
            throw new InvalidOperationException("AT authenticated subject/PDS/issuer mismatch.");
    }

    internal static void RequireScopes(string? granted, IEnumerable<string> allowed)
    {
        var ceiling = allowed.ToHashSet(StringComparer.Ordinal);
        var actual = (granted ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!actual.Contains(Constants.BaseScope, StringComparer.Ordinal) || actual.Any(x => !ceiling.Contains(x)))
            throw new InvalidOperationException("AT session permissions exceed the current declared ceiling; reauthorize with supported scopes.");
    }

    internal static void RequireRequestedScopes(string? granted, IEnumerable<string> requested)
    {
        var expected = requested.ToHashSet(StringComparer.Ordinal);
        RequireScopes(granted, expected);
        var actual = (granted ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (!expected.IsSubsetOf(actual)) throw new AtprotoMissingScopesException();
    }

    internal static string[] ScopesForSignIn(bool explicitlyRequested, IEnumerable<string> requested, string? preserved, IEnumerable<string> allowed)
    {
        var ceiling = allowed.ToArray();
        var selected = requested.Distinct(StringComparer.Ordinal).ToArray();
        if (!selected.Contains(Constants.BaseScope, StringComparer.Ordinal) || selected.Any(x => !ceiling.Contains(x, StringComparer.Ordinal)))
            throw new InvalidOperationException("Requested AT permissions exceed this client's declared scopes.");
        if (explicitlyRequested || !HasBroaderScopes(preserved)) return selected;
        RequireScopes(preserved, ceiling);
        return preserved!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Returns durable session metadata only after its scope ceiling and current DID/PDS/issuer binding pass. Null when
    /// the DID has no session this application can use: none, one granted to a previous client identity (WEB-0081), one
    /// that expired, or one this application's key ring cannot read.
    /// </summary>
    public async Task<SessionMetadata?> GetSessionMetadata(string did, CancellationToken ct = default)
    {
        if (!IdentityResolver.IsValidDid(did)) throw new ArgumentException("A valid DID is required.", nameof(did));
        if (PublishedClientId is not { } clientId) return null;
        var stored = await store.Read(did, ct);
        if (stored?.Grant is not { } saved || !Current(saved, clientId)) return null;
        var identity = await Resolve(did, ct);
        ValidateSaved(saved, identity, clientId);
        // Restore the OAuth session without issuing a source operation. Scope presence is not source freshness.
        using (await Rent(did, clientId, stored, identity, ct)) { }
        return new SessionMetadata(did, identity.Pds, identity.Issuer,
            saved.TokenSet.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    internal async Task<string> BrowserProperties(string state, CancellationToken ct)
    {
        var pending = await store.Peek(state, ct) ?? throw new AtprotoSignInException(SignInFailure.CallbackMismatch, "Invalid or expired AT authorization state.");
        return JsonSerializer.Deserialize<Binding>(pending.AppState!)!.Properties;
    }

    internal async Task<ClaimsPrincipal> Complete(string query, CancellationToken ct)
    {
        var parameters = HttpUtility.ParseQueryString(query);
        var state = parameters["state"] ?? "";
        var pending = await store.Peek(state, ct) ?? throw new AtprotoSignInException(SignInFailure.CallbackMismatch, "Invalid or expired AT authorization state.");
        var binding = JsonSerializer.Deserialize<Binding>(pending.AppState!)!;
        if (parameters["error"] is { } refused)
        {
            // The provider answered our own state with an error instead of a code: the person refused, or the provider
            // could not finish. Either way the sign-in is over; the state is spent and the code says which.
            await store.ConsumeAsync(state, ct);
            throw new AtprotoSignInException(refused == "access_denied" ? SignInFailure.AccessDenied : SignInFailure.Failed,
                "AT authorization was not granted.");
        }
        if (parameters.GetValues("state")?.Length != 1 || parameters.GetValues("iss")?.Length != 1 || parameters["iss"] != binding.Issuer || pending.Issuer != binding.Issuer)
        { await store.ConsumeAsync(state, ct); throw new AtprotoSignInException(SignInFailure.CallbackMismatch, "AT callback issuer mismatch."); }
        // The SDK's session writes stay here until browser, DID, PDS, issuer and grant checks all pass.
        var capture = new SessionCapture(verifyFirst: binding.Did is null ? async (value, token) =>
        {
            // The SDK assumes the initial server is the PDS. Resolve the authenticated
            // subject before it constructs its client, and reject an unrelated issuer.
            var resolved = await Resolve(value.TokenSet.Sub, token);
            RequireSubjectBinding(null, null, binding.Issuer, resolved.Did, resolved.Pds, resolved.Issuer, resolved.Pds);
            value.TokenSet.Audience = resolved.Pds;
        } : null);
        var clientId = ClientId;
        var oauth = OAuth(clientId, binding.Scope, binding.Did ?? binding.Issuer, capture);
        ATProtoOAuthClient? client = null;
        var cached = false;
        try
        {
            client = await oauth.CallbackAsync(AtprotoClientIdentity.RedirectUri(clientId) + query, ct);
            var identity = await Resolve(client.Did, ct);
            RequireSubjectBinding(binding.Did, binding.Pds, binding.Issuer, identity.Did, identity.Pds, identity.Issuer, client.BaseUrl.ToString());
            var granted = capture.Take();
            if (granted?.TokenSet.Sub != client.Did) throw new InvalidOperationException("Unverified AT session cannot be persisted.");
            RequireScopes(granted.TokenSet.Scope, AllowedScopes);
            RequireRequestedScopes(granted.TokenSet.Scope, binding.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (await Commit(client.Did, granted, binding, identity, clientId, ct) is { } committed)
            {
                clients.Admit(client.Did, new AtprotoClient(clientId, committed.Row.Grant!, identity.Pds, identity.Issuer, oauth, client, capture)).Dispose();
                cached = true;
            }
            return Principal(identity.Did, identity.Handle);
        }
        finally
        {
            if (!cached) { client?.Dispose(); oauth.Dispose(); }
        }
    }

    /// <summary>
    /// Makes a verified grant the DID's session, deciding on the row as read and committing only while the row is unchanged,
    /// so a refresh or a sign-in that lands in between is never overwritten by a decision made without it. An identity-only
    /// sign-in keeps a validated broader grant the session already holds, and null says so. A row that keeps changing fails
    /// the sign-in after a bounded number of reads.
    /// </summary>
    private async Task<StoredSession?> Commit(string did, OAuthSessionData granted, Binding binding,
        (string Did, string? Handle, string Pds, string Issuer) identity, string clientId, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var stored = await store.Read(did, ct);
            if (binding.Did is null && !HasBroaderScopes(binding.Scope) && stored?.Grant is { } saved
                && Current(saved, clientId) && HasBroaderScopes(saved.TokenSet.Scope))
            {
                ValidateSaved(saved, identity, clientId);
                return null;
            }

            if (await store.Commit(did, granted, stored, ct) is { } committed) return committed;
            if (attempt == Constants.CommitAttempts)
                throw new AtprotoSignInException(SignInFailure.Failed, "The AT session changed under every attempt to store this sign-in.");
        }
    }

    /// <summary>
    /// The DID's client, rented while the session row still holds the grant it was built from, else built from the row:
    /// its identity resolved and checked again, its session restored. A client built for another client identity
    /// (WEB-0081), or holding a grant the row no longer does, is retired and rebuilt.
    /// </summary>
    private async Task<ClientCache<AtprotoClient>.Lease> Rent(string did, string clientId, StoredSession stored,
        (string Did, string? Handle, string Pds, string Issuer)? resolved, CancellationToken ct)
    {
        var grant = stored.Row.Grant!;
        if (clients.TryRent(did, client => client.ClientId == clientId && client.Grant == grant) is { } cached) return cached;
        var identity = resolved ?? await Resolve(did, ct);
        ValidateSaved(stored.Grant!, identity, clientId);
        var capture = new SessionCapture(stored.Grant);
        var oauth = OAuth(clientId, stored.Grant!.Scope ?? Constants.BaseScope, did, capture);
        ATProtoOAuthClient? client = null;
        try
        {
            client = await oauth.RestoreSessionAsync(did, ct) ?? throw new InvalidOperationException("AT session restoration failed; reauthorize.");
            return clients.Admit(did, new AtprotoClient(clientId, grant, identity.Pds, identity.Issuer, oauth, client, capture));
        }
        catch { client?.Dispose(); oauth.Dispose(); throw; }
    }

    /// <summary>
    /// The DID's client with an access token that is not due. A grant that is due is claimed on its row first, so of every
    /// process sharing the store one refreshes it at its provider; the others read the row again until it holds the stored
    /// grant or the claim's lease ended, and take the claim then. A call that gets neither within <see cref="WaitBound"/>
    /// gives up with a <see cref="TimeoutException"/>, to be tried again, having written nothing.
    /// </summary>
    private async Task<ClientCache<AtprotoClient>.Lease> Ready(string did, string clientId, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + WaitBound;
        while (true)
        {
            var stored = await store.Read(did, ct);
            if (stored?.Grant is not { } grant || !Current(grant, clientId)) throw AuthorizationRequired();
            var lease = await Rent(did, clientId, stored, null, ct);
            try
            {
                if (!grant.TokenSet.IsExpired(Constants.RefreshAhead))
                {
                    RequireScopes(grant.TokenSet.Scope, AllowedScopes);
                    return lease;
                }

                if (!stored.Claimed)
                {
                    await using var claim = await store.Claim(stored, ClaimLease, ct);
                    if (claim is not null)
                    {
                        if (await Refresh(lease.Client, claim) is { } refreshed)
                        {
                            RequireScopes(refreshed.TokenSet.Scope, AllowedScopes);
                            return lease;
                        }

                        // A sign-in or a disconnect replaced the row during the refresh: this client's tokens are not
                        // the stored ones.
                        lease.Retire();
                    }
                }
            }
            catch (TimeoutException)
            {
                // The store did not take the result: this client alone holds the rotated tokens, and stays for the next
                // call to store them.
                lease.Dispose();
                throw;
            }
            catch
            {
                lease.Retire();
                lease.Dispose();
                throw;
            }

            lease.Dispose();
            await Pause(deadline, ct);
        }
    }

    /// <summary>
    /// Refreshes a claimed grant at its provider and stores what it returns; null when the row holds another grant by then,
    /// so the caller reads it again. Once sent, the request runs to its answer whatever the caller's cancellation, since
    /// the provider rotates the refresh token as it answers. A provider that refuses the grant has ended the session: its
    /// row ends too, and the person signs in again. A client still holding a grant the store did not take stores that one,
    /// with no request.
    /// </summary>
    private async Task<OAuthSessionData?> Refresh(AtprotoClient client, SessionClaim claim)
    {
        if (client.Capture.Written is null)
        {
            try { await client.Tokens.RefreshAsync(CancellationToken.None); }
            catch (TokenRefreshException error) when (error.InnerException is OAuthException { ErrorCode: "invalid_grant" })
            {
                await claim.End();
                throw AuthorizationRequired(error);
            }
        }

        var refreshed = client.Capture.Written ?? throw new InvalidOperationException("The AT session refresh returned no grant.");
        if (await claim.Refreshed(refreshed) is not { } stored) return null;
        client.Capture.Take();
        client.Grant = stored.Row.Grant!;
        return refreshed;
    }

    /// <summary>The longest one request waits behind another's claim: one request timeout, 30 s with the defaults.</summary>
    private TimeSpan WaitBound => TimeSpan.FromSeconds(options.RequestTimeoutSeconds);

    /// <summary>
    /// How long a claim stands: the holder's provider requests, each within the request timeout, and the write that settles
    /// them. With the defaults, 2 × 30 s + 10 s: 70 s, the longest a holder that stopped keeps others out.
    /// </summary>
    private TimeSpan ClaimLease => WaitBound * Constants.ClaimedRequests + Constants.ClaimWriteBound;

    // One pause behind another request's claim. Past the deadline the call fails, to be tried again, having written nothing.
    private static async Task Pause(DateTimeOffset deadline, CancellationToken ct)
    {
        var left = deadline - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero) throw new TimeoutException("Another request is refreshing this AT sign-in; try again.");
        await Task.Delay(left < Constants.ClaimPause ? left : Constants.ClaimPause, ct);
    }

    private static InvalidOperationException AuthorizationRequired(Exception? cause = null)
        => new("AT authorization is required for this DID.", cause);

    private void ValidateSaved(OAuthSessionData saved, (string Did, string? Handle, string Pds, string Issuer) identity, string clientId)
    {
        RequireScopes(saved.Scope, AllowedScopes);
        RequireScopes(saved.TokenSet.Scope, AllowedScopes);
        if (saved.TokenSet.Sub != identity.Did || saved.TokenSet.Audience.TrimEnd('/') != identity.Pds || saved.TokenSet.Issuer != identity.Issuer || saved.ClientId != clientId)
            throw new InvalidOperationException("Stored AT session no longer matches its identity, PDS, issuer or client; reauthorize.");
    }

    private static bool HasBroaderScopes(string? scope)
        => (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(x => x != Constants.BaseScope);

    /// <summary>
    /// Send XRPC to the DID's verified PDS through the guarded transport. The response is returned once its headers
    /// arrive and its body streams: no more than <paramref name="maximumResponseBytes"/> of it is read, or all of it when
    /// that is null, for a caller that streams. The caller owns response disposal. Requests of one DID run concurrently;
    /// a refresh they need is claimed on the session row, so one process makes it.
    /// </summary>
    public async Task<HttpResponseMessage> Send(string did, string nsid, HttpMethod method, long? maximumResponseBytes,
        IEnumerable<KeyValuePair<string, string>>? parameters = null, HttpContent? content = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nsid) || nsid.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')) || !nsid.Contains('.'))
            throw new ArgumentException("A valid XRPC NSID is required.", nameof(nsid));
        using var lease = await Ready(did, ClientId, ct);
        try
        {
            var client = lease.Client;
            var url = client.BaseUrl.ToString().TrimEnd('/') + "/xrpc/" + nsid;
            if (parameters is not null) url += "?" + string.Join('&', parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
            var body = content is null ? null : await content.ReadAsByteArrayAsync(ct);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var request = await client.Tokens.CreateDPoPRequestAsync(method, url);
                if (body is not null)
                {
                    request.Content = new ByteArrayContent(body);
                    foreach (var header in content!.Headers) request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                var response = await network.Send(request, maximumResponseBytes, ct);
                client.Tokens.UpdateNonceFromResponse(response, url);
                if (attempt == 0 && response.StatusCode == HttpStatusCode.Unauthorized && response.Headers.Contains("DPoP-Nonce")) { response.Dispose(); continue; }
                return response;
            }
            throw new HttpRequestException("AT request retry exhausted.");
        }
        catch
        {
            // A failed send disposes its client; the next request builds another from the session row.
            lease.Retire();
            throw;
        }
    }

    /// <summary>
    /// Revoke and remove the upstream grant; this is separate from local cookie logout. The client and the issuer's
    /// revocation endpoint are read first and the grant then claimed on its row, so no process refreshes it while it is
    /// revoked, and the claim covers the revocation request and its nonce retry alone. A call that has not claimed the row
    /// within the wait bound gives up, to be tried again. A session granted to a client identity this application no longer
    /// publishes (WEB-0081) is removed here whether or not the provider accepts its revocation; a current one whose
    /// revocation cannot be confirmed is kept for a retry. A session that changed while it was disconnected — signed in
    /// again, or claimed anew by another process — is not ended, and the call says so.
    /// </summary>
    public async Task Disconnect(string did, CancellationToken ct = default)
    {
        var clientId = PublishedClientId;
        var deadline = DateTimeOffset.UtcNow + WaitBound;
        while (true)
        {
            var stored = await store.Read(did, ct);
            if (stored?.Grant is not { } saved) throw AuthorizationRequired();
            if (!stored.Claimed)
            {
                var current = clientId is not null && Current(saved, clientId);
                using var lease = current ? await Rent(did, clientId!, stored, null, ct) : null;
                var endpoint = current ? await RevocationEndpoint(saved, ct) : null;
                using var stale = current ? null : await PrepareStale(did, saved, ct);
                await using var claim = await store.Claim(stored, ClaimLease, ct);
                if (claim is not null)
                {
                    if (current) await Revoke(lease!.Client.Tokens, endpoint!, saved, clientId!, ct);
                    else if (stale is not null) await RevokeStale(stale, saved, ct);
                    if (!await claim.End()) throw ChangedWhileDisconnected();
                    clients.Remove(did);
                    return;
                }
            }

            await Pause(deadline, ct);
        }
    }

    private static InvalidOperationException ChangedWhileDisconnected()
        => new("The AT session changed while it was disconnected — it was signed in again, or another process claimed it — " +
               "so it was not ended. Disconnect again if it should end.");

    // The issuer's revocation endpoint, read before the session is claimed. Throws when revocation cannot be confirmed, so
    // the session is kept for a retry.
    private async Task<string> RevocationEndpoint(OAuthSessionData saved, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(saved.TokenSet.RefreshToken)) throw new InvalidOperationException("Session has no refresh token to revoke.");
        using var discovery = new AuthorizationServerDiscovery(network.Client, TimeSpan.Zero);
        var metadata = await discovery.GetMetadataAsync(saved.TokenSet.Issuer, ct);
        return metadata.RevocationEndpoint ?? throw new InvalidOperationException("Provider does not advertise revocation; upstream disconnect cannot be confirmed.");
    }

    // Revoke the session's refresh token at its issuer, in the name of the client identity it was granted to: the request
    // and its nonce retry. Throws when the revocation cannot be confirmed, so the caller keeps the session for a retry.
    private async Task Revoke(DPoPTokenProvider provider, string endpoint, OAuthSessionData saved, string clientId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = await provider.CreateDPoPRequestAsync(HttpMethod.Post, endpoint, includeAccessToken: false);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            { ["token"] = saved.TokenSet.RefreshToken!, ["token_type_hint"] = "refresh_token", ["client_id"] = clientId });
            using var response = await network.Client.SendAsync(request, ct);
            provider.UpdateNonceFromResponse(response, endpoint);
            if (attempt == 0 && response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized && response.Headers.Contains("DPoP-Nonce")) continue;
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Provider rejected AT revocation; the local session was retained for retry.");
            return;
        }
        throw new InvalidOperationException("AT revocation nonce negotiation failed; the local session was retained for retry.");
    }

    // WEB-0081: best effort for a session granted to a previous client identity. A provider that authenticates a public
    // client by the metadata at its client_id may refuse, since this application now publishes another identity there;
    // the grant then lapses on the provider's terms, and the session is removed here regardless. What the revocation
    // needs is read before the session is claimed; null when it cannot be.
    private async Task<StaleRevocation?> PrepareStale(string did, OAuthSessionData saved, CancellationToken ct)
    {
        if (saved.ClientId is not { } clientId) return null;
        DPoPTokenProvider? provider = null;
        try
        {
            var memory = new MemoryOAuthSessionStore();
            await memory.StoreAsync(did, saved, ct);
            provider = new DPoPTokenProvider(network.Client, memory, clientId: clientId, redirectUri: saved.RedirectUri, scope: saved.Scope);
            if (!await provider.RestoreSessionAsync(did, ct)) { provider.Dispose(); return null; }
            return new StaleRevocation(provider, await RevocationEndpoint(saved, ct), clientId);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            provider?.Dispose();
            return null;
        }
    }

    private async Task RevokeStale(StaleRevocation stale, OAuthSessionData saved, CancellationToken ct)
    {
        try { await Revoke(stale.Provider, stale.Endpoint, saved, stale.ClientId, ct); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Best effort by decision; see PrepareStale.
        }
    }

    // A previous client identity's provider and revocation endpoint, read before the claim.
    private sealed record StaleRevocation(DPoPTokenProvider Provider, string Endpoint, string ClientId) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    // WEB-0081: whether a stored session was granted to the client identity this application publishes now. One granted to
    // a previous identity — the base URL changed since — is gone: requests treat it as absent, sign-in authorizes afresh
    // and replaces it, and Disconnect removes it. It is kept until then, so accepting the previous URL again restores it.
    private static bool Current(OAuthSessionData saved, string clientId)
        => string.Equals(saved.ClientId, clientId, StringComparison.Ordinal);

    private sealed record Binding(string? Did, string? Pds, string Issuer, string Scope, string Properties);
}
