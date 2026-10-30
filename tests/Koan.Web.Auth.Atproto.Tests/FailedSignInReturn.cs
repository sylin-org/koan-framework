using System.Net;
using Koan.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// WEB-0080: a sign-in that cannot complete lands on the application's own page. The challenge names the landing
/// beside <c>return</c> (<c>failure=/sign-in</c>) or the application configures one for every provider
/// (<c>Koan:Web:Auth:ReturnUrl:FailurePath</c>); the person arrives there, under the base path, with one sanitized
/// code in <c>error</c>, uncached, and never the provider's body. A landing that is not a rooted path of the
/// application, that could leave its base path through a dot segment or an encoded slash, or whose query already names
/// <c>error</c>, is refused at the challenge. With nothing named, the connector's previous answers stand, and the page's link carries the base
/// path. The provider is a loopback fixture admitted through the Development gate; no public network is touched.
/// </summary>
public sealed class FailedSignInReturn
{
    private const string Did = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Handle = "alice.test";
    private const string Marker = "FIXTURE-PROVIDER-BODY";
    private const string PathBase = "/tangent";

    [Theory]
    [InlineData(false, false, "handle_unresolved")]
    [InlineData(true, false, "provider_unreachable")]
    public async Task A_challenge_that_cannot_start_lands_on_the_landing_named_beside_return(bool knowsDid, bool healthy, string code)
    {
        await using var provider = await Provider.Start(knowsDid, healthy);
        await using var host = await KoanHost.Start(provider.Origin);
        using var client = host.Client;

        var response = await client.GetAsync($"{PathBase}/auth/atproto/challenge?identifier={Handle}&return={PathBase}/home&failure=/sign-in", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{PathBase}/sign-in?error={code}", response.Headers.Location!.ToString());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.DoesNotContain(Marker, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_landing_that_differs_from_the_base_path_only_in_case_is_placed_under_it()
    {
        await using var provider = await Provider.Start(knowsDid: false, healthy: false);
        await using var host = await KoanHost.Start(provider.Origin);
        using var client = host.Client;

        var response = await client.GetAsync($"{PathBase}/auth/atproto/challenge?identifier={Handle}&return={PathBase}/home&failure=/TANGENT/x", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{PathBase}/TANGENT/x?error=handle_unresolved", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task A_callback_that_does_not_match_lands_on_the_configured_landing()
    {
        await using var provider = await Provider.Start(knowsDid: true, healthy: false);
        await using var host = await KoanHost.Start(provider.Origin, new() { ["Koan:Web:Auth:ReturnUrl:FailurePath"] = "/sign-in" });
        using var client = host.Client;

        var response = await client.GetAsync($"{PathBase}/auth/atproto/callback?state=unknown&code=x&iss={Uri.EscapeDataString(provider.Origin)}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{PathBase}/sign-in?error=callback_mismatch", response.Headers.Location!.ToString());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task A_refusal_at_the_provider_lands_with_access_denied_and_the_landing_keeps_its_query()
    {
        await using var provider = await Provider.Start(knowsDid: true, healthy: true);
        await using var host = await KoanHost.Start(provider.Origin);
        using var client = host.Client;

        var challenge = await client.GetAsync($"{PathBase}/auth/atproto/challenge?identifier={Handle}&return={PathBase}/home&failure=/sign-in?from=home", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.StartsWith(provider.Origin + "/authorize?", challenge.Headers.Location!.ToString());
        Assert.NotNull(provider.Pushed);
        var state = provider.Pushed!["state"].ToString();
        var correlation = challenge.Headers.GetValues("Set-Cookie").Select(static cookie => cookie.Split(';', 2)[0]).ToArray();
        Assert.Contains(correlation, static cookie => cookie.StartsWith(".AspNetCore.Correlation.", StringComparison.Ordinal));

        using var callback = new HttpRequestMessage(HttpMethod.Get,
            $"{PathBase}/auth/atproto/callback?state={Uri.EscapeDataString(state)}&iss={Uri.EscapeDataString(provider.Origin)}&error=access_denied&error_description={Uri.EscapeDataString(Marker)}");
        callback.Headers.Add("Cookie", string.Join("; ", correlation));
        var response = await client.SendAsync(callback, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{PathBase}/sign-in?from=home&error=access_denied", response.Headers.Location!.ToString());
        Assert.DoesNotContain(Marker, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("https://evil.example/sign-in")]
    [InlineData("//evil.example/sign-in")]
    [InlineData("sign-in")]
    [InlineData("/sign\\in")]
    [InlineData("/../admin")]
    [InlineData("/%2e%2e/admin")]
    [InlineData("/a/./b")]
    [InlineData("/a%2Fb")]
    [InlineData("/sign-in?error=forged")]
    public async Task A_landing_that_is_not_a_rooted_path_is_refused_at_the_challenge(string landing)
    {
        await using var provider = await Provider.Start(knowsDid: true, healthy: true);
        await using var host = await KoanHost.Start(provider.Origin);
        using var client = host.Client;

        var response = await client.GetAsync($"{PathBase}/auth/atproto/challenge?identifier={Handle}&return={PathBase}/home&failure={Uri.EscapeDataString(landing)}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'failure'", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Null(provider.Pushed);
    }

    [Fact]
    public async Task Without_a_landing_the_previous_answers_stand_and_the_page_link_carries_the_base_path()
    {
        await using var provider = await Provider.Start(knowsDid: false, healthy: false);
        await using var host = await KoanHost.Start(provider.Origin);
        using var client = host.Client;

        var challenge = await client.GetAsync($"{PathBase}/auth/atproto/challenge?identifier={Handle}&return={PathBase}/home", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, challenge.StatusCode);
        Assert.Equal("no-store", challenge.Headers.CacheControl!.ToString());
        var page = await challenge.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains($"href=\"{PathBase}/\"", page);
        Assert.DoesNotContain(Marker, page);

        var callback = await client.GetAsync($"{PathBase}/auth/atproto/callback?state=unknown&code=x", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, callback.StatusCode);
        Assert.Contains("atproto_authentication_failed", await callback.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A loopback PLC directory and provider: it knows the DID or not, and its OAuth discovery works or answers 503 with a body.</summary>
    private sealed class Provider : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private Provider(WebApplication app, string origin) { _app = app; Origin = origin; }

        public string Origin { get; }
        public IFormCollection? Pushed { get; private set; }

        public static async Task<Provider> Start(bool knowsDid, bool healthy)
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
            app.MapGet("/{**rest}", (string rest) =>
            {
                if (rest == Did)
                    return knowsDid
                        ? Results.Json(new
                        {
                            id = Did,
                            alsoKnownAs = new[] { "at://" + Handle },
                            verificationMethod = Array.Empty<object>(),
                            service = new[] { new { id = "#atproto_pds", type = "AtprotoPersonalDataServer", serviceEndpoint = origin } }
                        })
                        : Results.Text(Marker, statusCode: 404);
                if (!healthy) return Results.Text(Marker, statusCode: 503);
                return rest switch
                {
                    ".well-known/oauth-protected-resource" => Results.Json(new { resource = origin, authorization_servers = new[] { origin } }),
                    ".well-known/oauth-authorization-server" => Results.Json(new
                    {
                        issuer = origin, authorization_endpoint = origin + "/authorize", token_endpoint = origin + "/token",
                        pushed_authorization_request_endpoint = origin + "/par", response_types_supported = new[] { "code" },
                        code_challenge_methods_supported = new[] { "S256" }, dpop_signing_alg_values_supported = new[] { "ES256" }
                    }),
                    _ => Results.Text(Marker, statusCode: 404),
                };
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

    /// <summary>An in-memory Koan host (TestServer) under <c>https://www.example.com/tangent</c> with the atproto provider eligible and the loopback provider admitted in Development.</summary>
    private sealed class KoanHost : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly string _folder;

        private KoanHost(IHost host, string folder) { _host = host; _folder = folder; }

        public HttpClient Client
        {
            get
            {
                var client = _host.GetTestClient();
                client.BaseAddress = new Uri("http://localhost");
                return client;
            }
        }

        public static async Task<KoanHost> Start(string providerOrigin, Dictionary<string, string?>? extra = null)
        {
            // The application's store, where the connector keeps its sessions and pending authorizations.
            var folder = Path.Combine(Path.GetTempPath(), "koan-atproto-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var settings = new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Development",
                ["Koan:BackgroundServices:Enabled"] = "false",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Koan:Web:ApplicationUrl"] = "https://www.example.com" + PathBase,
                ["Koan:Web:Auth:Providers:atproto:Scopes:0"] = "atproto",
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={Path.Combine(folder, "store.sqlite")};Pooling=False",
                ["Koan:Web:Auth:Atproto:DevelopmentAllowedOrigins:0"] = providerOrigin,
                ["Koan:Web:Auth:Atproto:DevelopmentPlcDirectory"] = providerOrigin,
                ["Koan:Web:Auth:Atproto:DevelopmentHandles:" + Handle] = Did,
            };
            if (extra is not null) foreach (var (key, value) in extra) settings[key] = value;

            var host = Host.CreateDefaultBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.UseEnvironment("Development");
                    web.UseContentRoot(AppContext.BaseDirectory);
                    web.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
                    web.ConfigureServices(services => services.AddKoan());
                    web.Configure(_ => { });
                })
                .Build();
            try { await host.StartAsync(TestContext.Current.CancellationToken); }
            catch
            {
                host.Dispose();
                try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
                throw;
            }
            return new KoanHost(host, folder);
        }

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync(CancellationToken.None);
            _host.Dispose();
            try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
        }
    }
}
