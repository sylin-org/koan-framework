using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Web.Extensions;
using Xunit;

namespace Koan.Web.Auth.Integration.Tests;

/// <summary>
/// SEC-0027 — under a base URL with a path, the cookie's login redirect sends a browser to Koan's challenge route under
/// that path, exactly once (also when the path is <c>/auth</c>, the route's own first segment), and the challenge
/// answers; the provider discovery document's <c>challengeUrl</c> carries the same path. The dev Test provider's schemes
/// are the providers.
/// </summary>
public sealed class LoginRedirectUnderPathSpec
{
    [Theory]
    [InlineData("/app")]
    [InlineData("/auth")]
    public async Task The_login_redirect_and_the_discovery_link_stay_under_the_path(string pathBase)
    {
        await using var app = await Start("https://www.example.com" + pathBase);
        using var browser = app.NewClient();

        using var protectedPage = new HttpRequestMessage(HttpMethod.Get, pathBase + "/edge/protected");
        protectedPage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var login = await browser.SendAsync(protectedPage, Ct);

        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = login.Headers.Location!.OriginalString;
        location.Should().MatchRegex("^" + pathBase + @"/auth/[a-z-]+/challenge\?", "the elected provider's challenge route gets the base once");
        QueryHelpers.ParseQuery(new Uri("http://host" + location).Query)["return"].ToString().Should().Be(pathBase + "/edge/protected");
        using var challenged = await browser.GetAsync(location, Ct);
        challenged.StatusCode.Should().Be(HttpStatusCode.Redirect, "the challenge route answers under the path");

        using var discovery = await browser.GetAsync(pathBase + "/.well-known/auth/providers", Ct);
        discovery.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync(Ct));
        var providers = doc.RootElement.EnumerateArray().ToArray();
        providers.Should().NotBeEmpty();
        foreach (var provider in providers)
            provider.GetProperty("challengeUrl").GetString().Should().Be($"{pathBase}/auth/{provider.GetProperty("id").GetString()}/challenge");
    }

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<App> Start(string applicationUrl)
    {
        var port = GrabFreePort();
        var listen = $"http://127.0.0.1:{port}";
        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:0",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Koan:Web:ApplicationUrl"] = applicationUrl,
            }))
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

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
