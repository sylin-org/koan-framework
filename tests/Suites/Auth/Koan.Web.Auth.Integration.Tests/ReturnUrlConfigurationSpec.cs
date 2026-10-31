using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
/// SEC-0029 §2 and §3 — the return configuration is checked when the application starts: an allow-list entry names an
/// origin and optionally a path under it, and nothing else; the default landing is an application-relative local path,
/// or an absolute URL under an allow-list entry. A value outside either rule refuses the start naming its key, so no
/// landing Koan writes can leave the application except through the allow-list. A valid default lands under the base.
/// </summary>
public sealed class ReturnUrlConfigurationSpec
{
    [Theory]
    [InlineData("https://app.example?next=1")]
    [InlineData("https://app.example/#top")]
    [InlineData("https://user@app.example")]
    [InlineData("app.example")]
    [InlineData("/callback")]
    public async Task An_allow_list_entry_that_is_not_an_origin_refuses_the_start(string entry)
    {
        var start = () => Start(new() { ["Koan:Web:Auth:ReturnUrl:AllowList:0"] = entry });
        (await start.Should().ThrowAsync<Exception>()).Which.ToString()
            .Should().Contain("Koan:Web:Auth:ReturnUrl:AllowList");
    }

    [Theory]
    [InlineData("//evil.example")]
    [InlineData(@"/\evil.example")]
    [InlineData("https://evil.example/")]
    public async Task A_default_path_that_could_leave_the_application_refuses_the_start(string defaultPath)
    {
        var start = () => Start(new() { ["Koan:Web:Auth:ReturnUrl:DefaultPath"] = defaultPath });
        (await start.Should().ThrowAsync<Exception>()).Which.ToString()
            .Should().Contain("Koan:Web:Auth:ReturnUrl:DefaultPath");
    }

    [Fact]
    public async Task A_default_path_lands_under_the_base_path()
    {
        await using var app = await Start(new() { ["Koan:Web:Auth:ReturnUrl:DefaultPath"] = "/home" }, "/notes");
        using var client = app.NewClient();

        (await app.SignInLanding(client, "/notes/auth/test/challenge")).Should().Be("/notes/home");

        using var logout = await client.PostAsync("/notes/auth/logout", null, TestContext.Current.CancellationToken);
        logout.StatusCode.Should().Be(HttpStatusCode.Redirect);
        logout.Headers.Location!.OriginalString.Should().Be("/notes/home");
    }

    /// <summary>
    /// SEC-0029 §2: a Unicode spelling of an allow-listed host (full-width <c>ａｐｐ</c>) is redirected to in the ASCII
    /// form that was compared, at the sign-in callback and at logout, never a 500 from a non-ASCII header; an absolute
    /// <c>DefaultPath</c> spelled that way starts and lands in the same ASCII form.
    /// </summary>
    [Fact]
    public async Task A_unicode_spelling_of_an_allowed_host_lands_on_its_ascii_form()
    {
        const string unicode = "https://ａｐｐ.example";
        await using var app = await Start(new()
        {
            ["Koan:Web:Auth:ReturnUrl:AllowList:0"] = "https://app.example",
            ["Koan:Web:Auth:ReturnUrl:DefaultPath"] = unicode + "/home",
        });
        using var client = app.NewClient();

        (await app.SignInLanding(client, $"/auth/test/challenge?return={Uri.EscapeDataString(unicode + "/x")}"))
            .Should().Be("https://app.example/x");
        (await app.SignInLanding(client, "/auth/test/challenge")).Should().Be("https://app.example/home");

        using var logout = await client.PostAsync($"/auth/logout?return={Uri.EscapeDataString(unicode + "/")}", null, TestContext.Current.CancellationToken);
        logout.StatusCode.Should().Be(HttpStatusCode.Redirect);
        logout.Headers.Location!.OriginalString.Should().Be("https://app.example/");
    }

    private static async Task<App> Start(Dictionary<string, string?> settings, string pathBase = "")
    {
        var port = GrabFreePort();
        var listen = $"http://127.0.0.1:{port}";
        var configuration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "localhost:0",
            ["Logging:LogLevel:Default"] = "Warning",
        };
        if (pathBase.Length > 0) configuration["Koan:Web:ApplicationUrl"] = listen + pathBase;
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
                });
                web.Configure(_ => { });
            })
            .Build();
        try { await host.StartAsync(TestContext.Current.CancellationToken); }
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
                using var resp = await client.GetAsync(url, TestContext.Current.CancellationToken);
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
