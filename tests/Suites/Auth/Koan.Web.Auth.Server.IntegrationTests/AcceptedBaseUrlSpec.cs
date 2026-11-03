using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Mcp.Options;
using Koan.Security.Trust.Issuer;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// WEB-0081 — with nothing configured, the URL the application accepted is the authorization server's issuer and the
/// root of every endpoint it advertises and of the dev token's audience; accepting another while the host runs moves all
/// of them on the next request, and the issuer stated outside a request with it. A token minted under the previous URL is
/// refused at <c>/mcp</c> by the application's resource edge, as it would be after a restart with the new URL configured,
/// and a token minted under the new one is accepted.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class AcceptedBaseUrlSpec : IClassFixture<AcceptedBaseUrlFixture>
{
    private readonly AcceptedBaseUrlFixture _fx;
    public AcceptedBaseUrlSpec(AcceptedBaseUrlFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    [Fact]
    public async Task Accepting_a_new_url_moves_the_issuer_the_endpoints_and_the_mcp_identity_without_a_restart()
    {
        var baseUrl = _fx.Services.GetRequiredService<KoanWebBaseUrl>();
        var issuer = _fx.Services.GetRequiredService<IIssuerIdentifier>();
        using var client = _fx.NewCookieClient();
        (await client.GetAsync("/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var firstToken = await Expect(client, AcceptedBaseUrlFixture.FirstUrl);
        issuer.Resolve().Should().Be(AcceptedBaseUrlFixture.FirstUrl, "outside a request the issuer is the accepted URL");
        (await Mcp(client, firstToken)).StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);

        await baseUrl.Accept(AcceptedBaseUrlFixture.SecondUrl, Ct);

        var secondToken = await Expect(client, AcceptedBaseUrlFixture.SecondUrl);
        issuer.Resolve().Should().Be(AcceptedBaseUrlFixture.SecondUrl);
        _fx.Store.Value.Should().Be(AcceptedBaseUrlFixture.SecondUrl);

        using (var stale = await Mcp(client, firstToken))
        {
            stale.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a token minted under the previous URL names another issuer and resource");
        }
        using (var fresh = await Mcp(client, secondToken))
        {
            fresh.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        }
    }

    private static Task<HttpResponseMessage> Mcp(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"spec","version":"1"}}}""",
            System.Text.Encoding.UTF8, "application/json");
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
    }

    private static async Task<string> Expect(HttpClient client, string root)
    {
        string accessToken;
        using (var metadata = await Json(client, "/.well-known/oauth-authorization-server"))
        {
            metadata.RootElement.GetProperty("issuer").GetString().Should().Be(root);
            metadata.RootElement.GetProperty("authorization_endpoint").GetString().Should().Be($"{root}/oauth/authorize");
            metadata.RootElement.GetProperty("token_endpoint").GetString().Should().Be($"{root}/oauth/token");
            metadata.RootElement.GetProperty("jwks_uri").GetString().Should().Be($"{root}/.well-known/jwks.json");
        }

        using (var token = await Json(client, "/oauth/dev-token"))
        {
            token.RootElement.GetProperty("resource").GetString().Should().Be(root, "the dev token is for the application, the resource its edge takes");
            accessToken = token.RootElement.GetProperty("access_token").GetString()!;
        }

        return accessToken;
    }

    private static async Task<JsonDocument> Json(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }
}

/// <summary>
/// A real Kestrel host in Development with no <c>Koan:Web:ApplicationUrl</c>, no <c>Issuer</c> and no <c>ResourceUri</c>,
/// whose application keeps an accepted URL in an <see cref="IApplicationUrlStore"/> that holds <see cref="FirstUrl"/>.
/// </summary>
public sealed class AcceptedBaseUrlFixture : IAsyncLifetime
{
    public const string FirstUrl = "https://first.example";
    public const string SecondUrl = "https://second.example";

    private IHost? _host;

    public MemoryApplicationUrlStore Store { get; } = new(FirstUrl);
    public int Port { get; private set; }
    public string ListenUrl => $"http://127.0.0.1:{Port}";
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

    public async ValueTask InitializeAsync()
    {
        Port = GrabFreePort();

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Development",
                ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                ["Koan:Data:Sources:Default:ConnectionString"] = "memory://accepted-base-url-tests",
                ["Koan:Web:Auth:ResourceServer:Enabled"] = "true",
                ["Koan:BackgroundServices:Enabled"] = "false",
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(ListenUrl);
                web.UseEnvironment("Development");
                web.ConfigureServices(s =>
                {
                    AppHost.Current = null;
                    s.AddSingleton<IApplicationUrlStore>(Store);
                    s.AddKoan();
                    s.AddKoanWeb();
                    s.AddKoanControllersFrom<TestSignInController>();
                    s.Configure<McpServerOptions>(o =>
                    {
                        o.EnableStreamableHttpTransport = true;
                        o.RequireAuthentication = true;
                    });
                });
                web.Configure(_ => { });
            })
            .Build();

        await _host.StartAsync();
    }

    /// <summary>A fresh non-redirecting client with its own cookie jar.</summary>
    public HttpClient NewCookieClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };
        return new HttpClient(handler) { BaseAddress = new Uri(ListenUrl) };
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    private static int GrabFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try { return ((IPEndPoint)l.LocalEndpoint).Port; }
        finally { l.Stop(); }
    }
}

/// <summary>An application's accepted-URL store kept in memory.</summary>
public sealed class MemoryApplicationUrlStore(string? value) : IApplicationUrlStore
{
    public string? Value { get; private set; } = value;

    public Task<string?> Load(CancellationToken ct) => Task.FromResult(Value);

    public Task Save(string applicationUrl, CancellationToken ct)
    {
        Value = applicationUrl;
        return Task.CompletedTask;
    }
}
