using System.Security.Claims;
using System.Web;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using Koan.Web.Auth.Connector.Atproto.Infrastructure;
using Koan.Web.Auth.Connector.Atproto.Options;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Koan.Web.Auth.Connector.Atproto.Storage;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// WEB-0081: a stored session granted to the client identity the application published before it accepted another URL
/// is gone. The person signs in again by either path — handle-first and server-first — and the new grant replaces it;
/// the session reads as absent; Disconnect removes it, revoking in the old identity's name where the provider allows.
/// A loopback PLC directory and provider stand in for the network; no public host is touched.
/// </summary>
public sealed class StaleClientIdentity
{
    private const string Did = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Handle = "bob.test";
    private const string FirstUrl = "https://first.example";
    private const string SecondUrl = "https://second.example";

    [Fact]
    public async Task Handle_first_sign_in_authorizes_afresh_under_the_new_identity()
    {
        await using var world = await World.Start();

        var authorization = await world.Sessions.Start(Handle, new AuthenticationProperties(), "browser", TestContext.Current.CancellationToken);

        Assert.Equal(World.ClientOf(SecondUrl), HttpUtility.ParseQueryString(new Uri(authorization).Query)["client_id"]);
        Assert.Equal(World.ClientOf(SecondUrl), world.Provider.Pushed!["client_id"].ToString());
        Assert.Equal("atproto", world.Provider.Pushed!["scope"].ToString());
    }

    [Fact]
    public async Task Server_first_sign_in_replaces_the_stale_grant()
    {
        await using var world = await World.Start();

        await world.Sessions.StartServer(world.Provider.Origin, new AuthenticationProperties(), "browser", TestContext.Current.CancellationToken);
        var state = world.Provider.Pushed!["state"].ToString();
        var principal = await world.Sessions.Complete(
            $"?state={Uri.EscapeDataString(state)}&iss={Uri.EscapeDataString(world.Provider.Origin)}&code=fixture-code",
            TestContext.Current.CancellationToken);

        Assert.Equal(Did, principal.FindFirstValue(ClaimTypes.NameIdentifier));
        var replaced = await world.Store.GetAsync(Did, TestContext.Current.CancellationToken);
        Assert.Equal(World.ClientOf(SecondUrl), replaced!.ClientId);
        Assert.Equal("atproto", replaced.TokenSet.Scope);
    }

    [Fact]
    public async Task The_stale_session_reads_as_absent()
    {
        await using var world = await World.Start();

        Assert.Null(await world.Sessions.GetSessionMetadata(Did, TestContext.Current.CancellationToken));
        var send = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            world.Sessions.Send(Did, "com.atproto.server.getSession", HttpMethod.Get, ct: TestContext.Current.CancellationToken));
        Assert.Equal("AT authorization is required for this DID.", send.Message);
        Assert.NotNull(await world.Store.GetAsync(Did, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disconnect_removes_the_stale_session_and_revokes_in_the_old_identity_s_name()
    {
        await using var world = await World.Start();

        await world.Sessions.Disconnect(Did, TestContext.Current.CancellationToken);

        Assert.Null(await world.Store.GetAsync(Did, TestContext.Current.CancellationToken));
        Assert.Equal(World.ClientOf(FirstUrl), world.Provider.Revoked!["client_id"].ToString());
        Assert.Equal("stale-refresh", world.Provider.Revoked!["token"].ToString());
    }

    [Fact]
    public async Task Disconnect_removes_the_stale_session_when_the_provider_refuses_the_revocation()
    {
        await using var world = await World.Start(revocation: false);

        await world.Sessions.Disconnect(Did, TestContext.Current.CancellationToken);

        Assert.Null(await world.Store.GetAsync(Did, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Sessions in Development whose base URL was <see cref="FirstUrl"/> when <see cref="Did"/> signed in with broader
    /// scopes, and is now <see cref="SecondUrl"/>.
    /// </summary>
    private sealed class World : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly AtprotoHttp _network;

        private World(Provider provider, ProtectedAtprotoStore store, AtprotoHttp network, AtprotoSessions sessions, string directory)
        {
            Provider = provider; Store = store; _network = network; Sessions = sessions; _directory = directory;
        }

        public Provider Provider { get; }
        public ProtectedAtprotoStore Store { get; }
        public AtprotoSessions Sessions { get; }

        public static string ClientOf(string baseUrl) => baseUrl + Constants.Metadata;

        public static async Task<World> Start(bool revocation = true)
        {
            var provider = await Provider.Start(revocation);
            var directory = Path.Combine(Path.GetTempPath(), "koan-atproto-" + Guid.NewGuid().ToString("N"));
            var options = new AtprotoOptions
            {
                SessionDirectory = directory, DevelopmentAllowedOrigins = [provider.Origin], DevelopmentPlcDirectory = provider.Origin,
            };
            options.DevelopmentHandles[Handle] = Did;
            var settings = Microsoft.Extensions.Options.Options.Create(options);
            var environment = new EnvironmentStub("Development");
            var store = new ProtectedAtprotoStore(DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "keys"))), settings, environment);
            var network = new AtprotoHttp(settings, environment);
            var baseUrl = new KoanWebBaseUrl(null, environment, store: new MemoryApplicationUrlStore());
            var sessions = new AtprotoSessions(store, network, settings, Microsoft.Extensions.Options.Options.Create(new AuthOptions()),
                new Catalog(), environment, baseUrl);

            await baseUrl.Accept(FirstUrl, TestContext.Current.CancellationToken);
            using var key = await DPoPKeyPair.GenerateAsync();
            await store.StoreAsync(Did, new OAuthSessionData
            {
                ClientId = ClientOf(FirstUrl),
                RedirectUri = FirstUrl + Constants.Callback,
                Scope = "atproto transition:generic",
                DPoPKey = key.ExportKeyPair(),
                TokenSet = new TokenSet
                {
                    Issuer = provider.Origin, Sub = Did, Audience = provider.Origin, Scope = "atproto transition:generic",
                    AccessToken = "stale-access", RefreshToken = "stale-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                },
            }, TestContext.Current.CancellationToken);
            await baseUrl.Accept(SecondUrl, TestContext.Current.CancellationToken);

            return new World(provider, store, network, sessions, directory);
        }

        public async ValueTask DisposeAsync()
        {
            Store.Dispose();
            _network.Dispose();
            await Provider.DisposeAsync();
            try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>A loopback PLC directory, PDS and authorization server: discovery, PAR, a token endpoint and revocation.</summary>
    private sealed class Provider : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private Provider(WebApplication app, string origin) { _app = app; Origin = origin; }

        public string Origin { get; }
        public IFormCollection? Pushed { get; private set; }
        public IFormCollection? Revoked { get; private set; }

        public static async Task<Provider> Start(bool revocation)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            var app = builder.Build();
            var origin = "";
            Provider? self = null;
            app.MapPost("/par", async (HttpContext context) =>
            {
                self!.Pushed = await context.Request.ReadFormAsync();
                return Results.Json(new { request_uri = "urn:fixture:request", expires_in = 60 }, statusCode: 201);
            });
            app.MapPost("/token", () => Results.Json(new
            {
                access_token = "fresh-access", token_type = "DPoP", expires_in = 3600, refresh_token = "fresh-refresh",
                scope = "atproto", sub = Did,
            }));
            app.MapPost("/revoke", async (HttpContext context) =>
            {
                self!.Revoked = await context.Request.ReadFormAsync();
                return Results.Ok();
            });
            app.MapGet("/{**rest}", (string rest) => rest switch
            {
                Did => Results.Json(new
                {
                    id = Did,
                    alsoKnownAs = new[] { "at://" + Handle },
                    verificationMethod = Array.Empty<object>(),
                    service = new[] { new { id = "#atproto_pds", type = "AtprotoPersonalDataServer", serviceEndpoint = origin } },
                }),
                ".well-known/oauth-protected-resource" => Results.Json(new { resource = origin, authorization_servers = new[] { origin } }),
                ".well-known/oauth-authorization-server" => Results.Json(new
                {
                    issuer = origin, authorization_endpoint = origin + "/authorize", token_endpoint = origin + "/token",
                    pushed_authorization_request_endpoint = origin + "/par", revocation_endpoint = revocation ? origin + "/revoke" : null,
                    response_types_supported = new[] { "code" }, code_challenge_methods_supported = new[] { "S256" },
                    dpop_signing_alg_values_supported = new[] { "ES256" },
                }),
                _ => Results.NotFound(),
            });
            await app.StartAsync();
            origin = app.Urls.Single();
            self = new Provider(app, origin);
            return self;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class MemoryApplicationUrlStore : IApplicationUrlStore
    {
        private string? _value;

        public Task<string?> Load(CancellationToken ct) => Task.FromResult(_value);

        public Task Save(string applicationUrl, CancellationToken ct)
        {
            _value = applicationUrl;
            return Task.CompletedTask;
        }
    }

    private sealed class Catalog : IAuthProviderCatalog
    {
        public IReadOnlyList<AuthProviderInfo> Providers { get; } =
            [new("atproto", "AT Protocol", "atproto", "enabled", true, true, false, 200, "Stale identity", null, null, ["atproto", "transition:generic"], "/auth/atproto/challenge")];
        public AuthProviderInfo? Default => Providers[0];
        public AuthProviderInfo? Find(string? id) => id == "atproto" ? Default : null;
    }

    private sealed class EnvironmentStub(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "AtprotoTests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
