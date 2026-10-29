using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Security.Trust.Issuer;
using Koan.Web.Extensions;
using Xunit;

namespace Koan.Web.Auth.Integration.Tests;

/// <summary>
/// SEC-0025 — the resource edge of ordinary requests, on a Kestrel host under <c>https://www.example.com/app</c> with
/// <c>Koan:Web:Auth:ResourceServer:Enabled</c>: a token issued for the base URL (with or without its trailing slash)
/// authenticates a page as its subject, under <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/>; a token
/// for another audience, or tampered, answers 401 with <c>Bearer error="invalid_token"</c> on an anonymous page and on
/// a protected one; a request with a <c>Bearer</c> header is never authenticated by the session cookie it also
/// carries; one without, or with a header of another scheme, keeps the cookie. A configured resource identifier replaces the base URL, and without the
/// setting nothing changes.
/// </summary>
public sealed class ResourceEdgeSpec
{
    private const string ApplicationUrl = "https://www.example.com/app";
    private const string PathBase = "/app";

    [Fact]
    public async Task A_token_for_the_base_url_authenticates_a_page_as_its_subject()
    {
        await using var app = await Start(new() { ["Koan:Web:Auth:ResourceServer:Enabled"] = "true" });
        using var client = app.NewClient();

        foreach (var audience in new[] { ApplicationUrl, ApplicationUrl + "/" })
        {
            using var res = await client.SendAsync(Get("/edge/whoami", app.Mint("bob", audience)), Ct);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = await Json(res);
            doc.RootElement.GetProperty("authenticated").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("id").GetString().Should().Be("bob", "the subject is the person's identifier");
            doc.RootElement.GetProperty("scheme").GetString().Should().Be("Koan.bearer");
        }
    }

    [Fact]
    public async Task A_token_for_another_audience_or_tampered_answers_401_invalid_token()
    {
        await using var app = await Start(new() { ["Koan:Web:Auth:ResourceServer:Enabled"] = "true" });
        using var client = app.NewClient();
        var good = app.Mint("bob", ApplicationUrl);
        var rejected = new[]
        {
            app.Mint("bob", ApplicationUrl + "/mcp"),
            app.Mint("bob", "https://elsewhere.example"),
            good[..^4] + (good.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA"),
            "not-a-token",
        };

        foreach (var token in rejected)
        {
            foreach (var path in new[] { "/edge/whoami", "/edge/protected" })
            {
                using var res = await client.SendAsync(Get(path, token), Ct);
                res.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{path} with a rejected token");
                res.Headers.WwwAuthenticate.ToString().Should().Be("Bearer error=\"invalid_token\"");
            }
        }
    }

    /// <summary>
    /// The edge engages only on a <c>Bearer</c> header. A reverse proxy that passes its own <c>Basic</c> credentials
    /// upstream leaves the request to the session: a signed-in person still reads as themselves, a request with nothing
    /// else reads as a visitor, and a rejected bearer still answers 401.
    /// </summary>
    [Fact]
    public async Task An_authorization_header_of_another_scheme_is_left_to_the_session()
    {
        await using var app = await Start(new() { ["Koan:Web:Auth:ResourceServer:Enabled"] = "true" });
        using var browser = app.NewClient();
        (await browser.GetAsync(PathBase + "/edge/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var path in new[] { "/edge/protected", "/edge/whoami" })
        {
            using var res = await browser.SendAsync(Basic(path), Ct);
            res.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} with a proxy's Basic header and a session");
            using var doc = await Json(res);
            doc.RootElement.GetProperty("id").GetString().Should().Be("alice", "the session authenticates");
        }

        using var visitor = app.NewClient();
        using (var res = await visitor.SendAsync(Basic("/edge/whoami"), Ct))
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = await Json(res);
            doc.RootElement.GetProperty("authenticated").GetBoolean().Should().BeFalse("a Basic header alone is a visitor");
        }

        using var rejected = await browser.SendAsync(Get("/edge/whoami", "not-a-token"), Ct);
        rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        rejected.Headers.WwwAuthenticate.ToString().Should().Be("Bearer error=\"invalid_token\"");
    }

    [Fact]
    public async Task A_request_with_an_authorization_header_is_never_authenticated_by_the_cookie()
    {
        await using var app = await Start(new() { ["Koan:Web:Auth:ResourceServer:Enabled"] = "true" });
        using var browser = app.NewClient();
        (await browser.GetAsync(PathBase + "/edge/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var cookieOnly = await browser.GetAsync(PathBase + "/edge/whoami", Ct))
        using (var doc = await Json(cookieOnly))
        {
            doc.RootElement.GetProperty("id").GetString().Should().Be("alice", "without a header the session cookie authenticates");
            doc.RootElement.GetProperty("scheme").GetString().Should().Be("Koan.cookie");
        }

        using (var bearer = await browser.SendAsync(Get("/edge/whoami", app.Mint("bob", ApplicationUrl)), Ct))
        using (var doc = await Json(bearer))
        {
            doc.RootElement.GetProperty("id").GetString().Should().Be("bob", "the bearer alone authenticates, whatever cookie rides along");
        }

        using var rejected = await browser.SendAsync(Get("/edge/whoami", app.Mint("bob", "https://elsewhere.example")), Ct);
        rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a rejected bearer never falls back to the cookie");
    }

    [Fact]
    public async Task A_configured_resource_identifier_replaces_the_base_url()
    {
        await using var app = await Start(new()
        {
            ["Koan:Web:Auth:ResourceServer:Enabled"] = "true",
            ["Koan:Web:Auth:ResourceServer:Resource"] = "urn:example:notes",
        });
        using var client = app.NewClient();

        (await client.SendAsync(Get("/edge/protected", app.Mint("bob", "urn:example:notes")), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.SendAsync(Get("/edge/protected", app.Mint("bob", ApplicationUrl)), Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Without_the_setting_a_bearer_on_an_ordinary_request_is_not_read()
    {
        await using var app = await Start(new());
        using var client = app.NewClient();

        using var res = await client.SendAsync(Get("/edge/whoami", app.Mint("bob", ApplicationUrl)), Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await Json(res);
        doc.RootElement.GetProperty("authenticated").GetBoolean().Should().BeFalse("the cookie stays the only scheme until the application opts in");
    }

    [Fact]
    public async Task A_resource_identifier_that_is_not_an_absolute_uri_refuses_the_start()
    {
        var start = () => Start(new()
        {
            ["Koan:Web:Auth:ResourceServer:Enabled"] = "true",
            ["Koan:Web:Auth:ResourceServer:Resource"] = "notes",
        });
        (await start.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("Koan:Web:Auth:ResourceServer:Resource");
    }

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpRequestMessage Get(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, PathBase + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage Basic(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, PathBase + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", "cHJveHk6c2VjcmV0");
        return request;
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

    private static async Task<App> Start(Dictionary<string, string?> settings)
    {
        var port = GrabFreePort();
        var listen = $"http://127.0.0.1:{port}";
        var configuration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "localhost:0",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Koan:Web:ApplicationUrl"] = ApplicationUrl,
        };
        foreach (var (key, value) in settings) configuration[key] = value;

        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(configuration))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(listen);
                web.UseEnvironment("Development");
                web.ConfigureServices(s =>
                {
                    s.AddDataProtection().UseEphemeralDataProtectionProvider();
                    s.AddKoan();
                    s.AddKoanWeb();
                    s.AddKoanControllersFrom<ResourceEdgeController>();
                });
                web.Configure(_ => { });
            })
            .Build();
        try { await host.StartAsync(Ct); }
        catch { host.Dispose(); throw; }
        return new App(host, listen);
    }

    private static int GrabFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class App(IHost host, string listen) : IAsyncDisposable
    {
        public HttpClient NewClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() })
        { BaseAddress = new Uri(listen) };

        public string Mint(string subject, string audience)
            => host.Services.GetRequiredService<IIssuer>().Issue(new TrustClaims { Subject = subject }, TimeSpan.FromMinutes(5), audience);

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
