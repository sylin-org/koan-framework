using System.Collections.Concurrent;
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
    private readonly ProtectedAtprotoStore store;
    private readonly AtprotoHttp network;
    private readonly AtprotoOptions options;
    private readonly IOptions<AuthOptions> auth;
    private readonly IAuthProviderCatalog catalog;
    private readonly KoanWebBaseUrl baseUrl;
    private readonly IHttpContextAccessor? http;
    private readonly bool development;
    private readonly ConcurrentDictionary<string, CachedClient> clients = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new();
    private readonly AtprotoDns dns = new();

    internal AtprotoSessions(ProtectedAtprotoStore store, AtprotoHttp network, IOptions<AtprotoOptions> options,
        IOptions<AuthOptions> auth, IAuthProviderCatalog catalog, IHostEnvironment environment, KoanWebBaseUrl baseUrl,
        IHttpContextAccessor? http = null)
    { this.store = store; this.network = network; this.options = options.Value; this.auth = auth; this.catalog = catalog; this.baseUrl = baseUrl; this.http = http; development = KoanEnv.Gate.DevelopmentOnly(environment); }

    /// <summary>
    /// The configured client-metadata URL, else the one derived at use from the application's base URL (WEB-0078): the
    /// configured or accepted one (WEB-0081), else the current request's where Koan derives it. Read at use, so a URL
    /// the application accepts changes the identity from the next request on.
    /// </summary>
    internal string ClientId
    {
        get
        {
            if (auth.Value.Providers.GetValueOrDefault(Constants.Provider)?.ClientId is { } configured) return configured;
            var root = BaseUrl();
            return AtprotoClientIdentity.Derive(root, AllowedScopes, development)
                ?? throw new InvalidOperationException(AtprotoClientIdentity.Correction(root, baseUrl.CanAccept && !baseUrl.IsConfigured));
        }
    }

    private string? BaseUrl()
        => baseUrl.Current ?? (baseUrl.DerivesFromRequests && http?.HttpContext is { } context ? baseUrl.Resolve(context) : null);
    internal string[] AllowedScopes => catalog.Find(Constants.Provider)?.Scopes.ToArray() ?? [Constants.BaseScope];
    internal string RedirectUri => AtprotoClientIdentity.RedirectUri(ClientId);
    internal bool Enabled => catalog.Find(Constants.Provider)?.Eligible == true;

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
    private OAuthSession OAuth(string clientId, string scope, string did, IOAuthSessionStore? sessions = null) => new(new OAuthClientConfig
    { ClientId = clientId, RedirectUri = AtprotoClientIdentity.RedirectUri(clientId), Scope = scope, HttpClient = network.Client,
        StateStore = store, SessionStore = sessions ?? store, IdentityResolver = Resolver(did) });

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
        string? verifiedHandle = null;
        if (document.Handle is { } handle)
        {
            if (development && options.DevelopmentHandles.TryGetValue(handle, out var mappedHandle))
            { if (mappedHandle == document.Id) verifiedHandle = handle; }
            else
            {
                try { if ((await resolver.ResolveAsync(handle, ct)).Id == document.Id) verifiedHandle = handle; }
                catch (Exception error) when (error is not OperationCanceledException) { /* DID sign-in does not depend on an optional display handle. */ }
            }
        }
        return (document.Id, verifiedHandle, pds.TrimEnd('/'), issuer);
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
        if (!explicitlyRequested)
        {
            var gate = locks.GetOrAdd(identity.Did, _ => new SemaphoreSlim(1));
            await gate.WaitAsync(ct);
            try
            {
                var saved = await store.GetAsync(identity.Did, ct);
                if (saved is not null && Current(saved, clientId) && HasBroaderScopes(saved.TokenSet.Scope))
                {
                    ValidateSaved(saved, identity);
                    scopes = ScopesForSignIn(false, scopes, saved.TokenSet.Scope, AllowedScopes);
                }
            }
            finally { gate.Release(); }
        }
        var scope = string.Join(' ', scopes.Distinct(StringComparer.Ordinal));
        var binding = new Binding(identity.Did, identity.Pds, identity.Issuer, scope, protectedProperties);
        using var oauth = OAuth(clientId, scope, identity.Did);
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
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
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
                using var response = await Classified(SignInFailure.ProviderUnreachable, "AT authorization request could not be pushed.", () => network.Send(request, ct));
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

    /// <summary>Returns durable session metadata only after its scope ceiling and current DID/PDS/issuer binding pass.</summary>
    public async Task<SessionMetadata?> GetSessionMetadata(string did, CancellationToken ct = default)
    {
        if (!IdentityResolver.IsValidDid(did)) throw new ArgumentException("A valid DID is required.", nameof(did));
        var gate = locks.GetOrAdd(did, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(ct);
        try
        {
            var saved = await store.GetAsync(did, ct);
            if (saved is null || !Current(saved, ClientId)) return null;
            var identity = await Resolve(did, ct);
            ValidateSaved(saved, identity);
            // Restore/refresh the OAuth session without issuing a source operation. Scope presence is not source freshness.
            await Client(did, ct);
            saved = await store.GetAsync(did, ct) ?? throw new InvalidOperationException("AT session disappeared during restoration; reauthorize.");
            ValidateSaved(saved, identity);
            return new SessionMetadata(did, identity.Pds, identity.Issuer,
                saved.TokenSet.Scope!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        finally { gate.Release(); }
    }

    internal string BrowserProperties(string state)
    {
        var pending = store.Peek(state) ?? throw new AtprotoSignInException(SignInFailure.CallbackMismatch, "Invalid or expired AT authorization state.");
        return JsonSerializer.Deserialize<Binding>(pending.AppState!)!.Properties;
    }

    internal async Task<ClaimsPrincipal> Complete(string query, CancellationToken ct)
    {
        var parameters = HttpUtility.ParseQueryString(query);
        var state = parameters["state"] ?? "";
        var pending = store.Peek(state) ?? throw new AtprotoSignInException(SignInFailure.CallbackMismatch, "Invalid or expired AT authorization state.");
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
        var staging = new VerifiedSessionStore(store, binding.Did is null ? async (value, token) =>
        {
            // The SDK assumes the initial server is the PDS. Resolve the authenticated
            // subject before it constructs its client, and reject an unrelated issuer.
            var resolved = await Resolve(value.TokenSet.Sub, token);
            RequireSubjectBinding(null, null, binding.Issuer, resolved.Did, resolved.Pds, resolved.Issuer, resolved.Pds);
            value.TokenSet.Audience = resolved.Pds;
        } : null);
        var clientId = ClientId;
        var oauth = OAuth(clientId, binding.Scope, binding.Did ?? binding.Issuer, staging);
        ATProtoOAuthClient? client = null;
        try
        {
            client = await oauth.CallbackAsync(AtprotoClientIdentity.RedirectUri(clientId) + query, ct);
            var identity = await Resolve(client.Did, ct);
            RequireSubjectBinding(binding.Did, binding.Pds, binding.Issuer, identity.Did, identity.Pds, identity.Issuer, client.BaseUrl.ToString());
            RequireScopes(staging.GrantedScope, AllowedScopes);
            RequireRequestedScopes(staging.GrantedScope, binding.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var gate = locks.GetOrAdd(client.Did, _ => new SemaphoreSlim(1));
            await gate.WaitAsync(ct);
            try
            {
                var saved = await store.GetAsync(client.Did, ct);
                var preserveGrant = binding.Did is null && !HasBroaderScopes(binding.Scope) && saved is not null
                    && Current(saved, clientId) && HasBroaderScopes(saved.TokenSet.Scope);
                if (preserveGrant)
                {
                    ValidateSaved(saved!, identity);
                    client.Dispose();
                    oauth.Dispose();
                }
                else
                {
                    await staging.Commit(client.Did, ct);
                    clients.TryGetValue(client.Did, out var replaced);
                    clients[client.Did] = new CachedClient(clientId, client);
                    replaced?.Client.Dispose();
                }
            }
            finally { gate.Release(); }
            var claims = new ClaimsIdentity(Constants.Provider);
            claims.AddClaim(new Claim(ClaimTypes.NameIdentifier, identity.Did));
            claims.AddClaim(new Claim("sub", identity.Did));
            claims.AddClaim(new Claim(AtprotoClaimTypes.Did, identity.Did));
            if (identity.Handle is { } handle) { claims.AddClaim(new Claim(AtprotoClaimTypes.Handle, handle)); claims.AddClaim(new Claim(ClaimTypes.Name, handle)); }
            return new ClaimsPrincipal(claims);
        }
        catch { client?.Dispose(); oauth.Dispose(); throw; }
    }

    private async Task<ATProtoOAuthClient> Client(string did, CancellationToken ct)
    {
        var clientId = ClientId;
        if (clients.TryGetValue(did, out var cached))
        {
            if (cached.ClientId == clientId) return cached.Client;
            // WEB-0081: the application's base URL changed since this client was built, so it speaks for a client
            // identity this application no longer publishes. The saved session is checked against the current one below.
            clients.TryRemove(did, out _);
            cached.Client.Dispose();
        }
        var saved = await store.GetAsync(did, ct);
        if (saved is null || !Current(saved, clientId)) throw new InvalidOperationException("AT authorization is required for this DID.");
        var identity = await Resolve(did, ct);
        ValidateSaved(saved, identity);
        var client = await OAuth(clientId, saved.Scope ?? Constants.BaseScope, did).RestoreSessionAsync(did, ct) ?? throw new InvalidOperationException("AT session restoration failed; reauthorize.");
        clients[did] = new CachedClient(clientId, client);
        return client;
    }

    private void ValidateSaved(OAuthSessionData saved, (string Did, string? Handle, string Pds, string Issuer) identity)
    {
        RequireScopes(saved.Scope, AllowedScopes);
        RequireScopes(saved.TokenSet.Scope, AllowedScopes);
        if (saved.TokenSet.Sub != identity.Did || saved.TokenSet.Audience.TrimEnd('/') != identity.Pds || saved.TokenSet.Issuer != identity.Issuer || saved.ClientId != ClientId)
            throw new InvalidOperationException("Stored AT session no longer matches its identity, PDS, issuer or client; reauthorize.");
    }

    private static bool HasBroaderScopes(string? scope)
        => (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(x => x != Constants.BaseScope);

    /// <summary>Send XRPC to the DID's verified PDS through the guarded transport. The caller owns response disposal.</summary>
    public async Task<HttpResponseMessage> Send(string did, string nsid, HttpMethod method,
        IEnumerable<KeyValuePair<string, string>>? parameters = null, HttpContent? content = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nsid) || nsid.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')) || !nsid.Contains('.'))
            throw new ArgumentException("A valid XRPC NSID is required.", nameof(nsid));
        var gate = locks.GetOrAdd(did, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(ct);
        try
        {
            var client = await Client(did, ct);
            var provider = (DPoPTokenProvider)client.TokenProvider;
            await provider.GetAccessTokenAsync(ct);
            var current = await store.GetAsync(did, ct) ?? throw new InvalidOperationException("AT session disappeared; reauthorize.");
            RequireScopes(current.TokenSet.Scope, AllowedScopes);
            var url = client.BaseUrl.ToString().TrimEnd('/') + "/xrpc/" + nsid;
            if (parameters is not null) url += "?" + string.Join('&', parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
            var body = content is null ? null : await content.ReadAsByteArrayAsync(ct);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var request = await provider.CreateDPoPRequestAsync(method, url);
                if (body is not null)
                {
                    request.Content = new ByteArrayContent(body);
                    foreach (var header in content!.Headers) request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                var response = await network.Client.SendAsync(request, ct);
                provider.UpdateNonceFromResponse(response, url);
                if (attempt == 0 && response.StatusCode == HttpStatusCode.Unauthorized && response.Headers.Contains("DPoP-Nonce")) { response.Dispose(); continue; }
                return response;
            }
            throw new HttpRequestException("AT request retry exhausted.");
        }
        catch { clients.TryRemove(did, out _); throw; }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Revoke and remove the upstream grant; this is separate from local cookie logout. A session granted to a client
    /// identity this application no longer publishes (WEB-0081) is removed here whether or not the provider accepts its
    /// revocation.
    /// </summary>
    public async Task Disconnect(string did, CancellationToken ct = default)
    {
        var gate = locks.GetOrAdd(did, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(ct);
        try
        {
            if (await store.GetAsync(did, ct) is { } stored && !Current(stored, ClientId))
            {
                await RevokeStale(did, stored, ct);
                await store.DeleteAsync(did, ct);
                if (clients.TryRemove(did, out var cached)) cached.Client.Dispose();
                return;
            }

            var client = await Client(did, ct);
            var provider = (DPoPTokenProvider)client.TokenProvider;
            var saved = await store.GetAsync(did, ct) ?? throw new InvalidOperationException("AT session unavailable.");
            await Revoke(provider, saved, ClientId, ct);
            await store.DeleteAsync(did, ct);
            clients.TryRemove(did, out _);
            client.Dispose();
        }
        finally { gate.Release(); }
    }

    // Revoke the session's refresh token at its issuer, in the name of the client identity it was granted to. Throws when
    // the revocation cannot be confirmed, so the caller keeps the session for a retry.
    private async Task Revoke(DPoPTokenProvider provider, OAuthSessionData saved, string clientId, CancellationToken ct)
    {
        using var discovery = new AuthorizationServerDiscovery(network.Client, TimeSpan.Zero);
        var metadata = await discovery.GetMetadataAsync(saved.TokenSet.Issuer, ct);
        var endpoint = metadata.RevocationEndpoint ?? throw new InvalidOperationException("Provider does not advertise revocation; upstream disconnect cannot be confirmed.");
        if (string.IsNullOrEmpty(saved.TokenSet.RefreshToken)) throw new InvalidOperationException("Session has no refresh token to revoke.");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = await provider.CreateDPoPRequestAsync(HttpMethod.Post, endpoint, includeAccessToken: false);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            { ["token"] = saved.TokenSet.RefreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = clientId });
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
    // the grant then lapses on the provider's terms, and the session is removed here regardless.
    private async Task RevokeStale(string did, OAuthSessionData saved, CancellationToken ct)
    {
        if (saved.ClientId is not { } clientId) return;
        try
        {
            var memory = new MemoryOAuthSessionStore();
            await memory.StoreAsync(did, saved, ct);
            using var provider = new DPoPTokenProvider(network.Client, memory, clientId: clientId,
                redirectUri: saved.RedirectUri, scope: saved.Scope);
            if (await provider.RestoreSessionAsync(did)) await Revoke(provider, saved, clientId, ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Best effort by decision; see above.
        }
    }

    // WEB-0081: whether a stored session was granted to the client identity this application publishes now. One granted to
    // a previous identity — the base URL changed since — is gone: requests treat it as absent, sign-in authorizes afresh
    // and replaces it, and Disconnect removes it. It is kept until then, so accepting the previous URL again restores it.
    private static bool Current(OAuthSessionData saved, string clientId)
        => string.Equals(saved.ClientId, clientId, StringComparison.Ordinal);

    private sealed record Binding(string? Did, string? Pds, string Issuer, string Scope, string Properties);

    // A restored or signed-in client and the client identity it was built for (WEB-0081).
    private sealed record CachedClient(string ClientId, ATProtoOAuthClient Client);
}
