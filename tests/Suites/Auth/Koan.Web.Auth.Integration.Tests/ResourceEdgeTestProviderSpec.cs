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
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Web.Extensions;
using Xunit;

namespace Koan.Web.Auth.Integration.Tests;

/// <summary>
/// SEC-0025 — a provider endpoint that checks a bearer of its own is not part of the application's resource. With the
/// resource edge on, the dev Test provider's OAuth handler calls its own userinfo endpoint with the provider's opaque
/// token; that endpoint carries <c>ResourceServerExemptAttribute</c>, so the sign-in completes, while an application
/// action still answers 401 <c>Bearer error="invalid_token"</c> to a token the edge rejects.
/// </summary>
public sealed class ResourceEdgeTestProviderSpec
{
    private const string PathBase = "/app";

    [Fact]
    public async Task With_the_edge_on_the_test_provider_signs_in_and_an_application_action_still_rejects_a_token()
    {
        await using var app = await Start();
        using var client = app.NewClient();

        (await app.SignInLanding(client, $"{PathBase}/auth/test/challenge?return={PathBase}/edge/whoami"))
            .Should().Be(PathBase + "/edge/whoami");
        using (var page = await client.GetAsync(PathBase + "/edge/whoami", Ct))
        using (var doc = JsonDocument.Parse(await page.Content.ReadAsStringAsync(Ct)))
        {
            doc.RootElement.GetProperty("authenticated").GetBoolean().Should().BeTrue("the sign-in completed");
            doc.RootElement.GetProperty("scheme").GetString().Should().Be("Koan.cookie");
        }

        using var rejected = new HttpRequestMessage(HttpMethod.Get, PathBase + "/edge/whoami");
        rejected.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
        using var answer = await client.SendAsync(rejected, Ct);
        answer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        answer.Headers.WwwAuthenticate.ToString().Should().Be("Bearer error=\"invalid_token\"");
    }

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<App> Start()
    {
        var port = GrabFreePort();
        var listen = $"http://127.0.0.1:{port}";
        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:0",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Koan:Web:ApplicationUrl"] = listen + PathBase,
                ["Koan:Web:Auth:ResourceServer:Enabled"] = "true",
                ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
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
        /// <summary>A cookie-aware, non-auto-redirecting client pre-seeded with the dev persona cookie.</summary>
        public HttpClient NewClient()
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() };
            handler.CookieContainer.Add(new Uri(listen), new Cookie("_tp_user", Uri.EscapeDataString("alice|alice@example.com")) { Path = "/" });
            return new HttpClient(handler) { BaseAddress = new Uri(listen) };
        }

        /// <summary>Follows challenge → authorize → callback and answers where the callback sends the person.</summary>
        public async Task<string> SignInLanding(HttpClient client, string challenge)
        {
            var url = new Uri(new Uri(listen), challenge);
            var chain = new List<string>();
            for (var hop = 0; hop < 8; hop++)
            {
                if (url.AbsolutePath.EndsWith("/authorize", StringComparison.Ordinal))
                    url = new Uri(QueryHelpers.AddQueryString(url.ToString(), "roles", "reader"));
                using var resp = await client.GetAsync(url, Ct);
                chain.Add($"{(int)resp.StatusCode} {url.PathAndQuery}");
                resp.StatusCode.Should().Be(HttpStatusCode.Redirect, string.Join(" -> ", chain));
                var location = resp.Headers.Location!;
                if (url.AbsolutePath.EndsWith("/callback", StringComparison.Ordinal)) return location.OriginalString;
                url = location.IsAbsoluteUri ? location : new Uri(url, location);
            }
            throw new InvalidOperationException("no callback within 8 hops: " + string.Join(" -> ", chain));
        }

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
