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
/// WEB-0080 — the failure landing is Web Auth's convention, so the maintained OAuth2 handler honours it too: with
/// <c>Koan:Web:Auth:ReturnUrl:FailurePath</c> set, a callback whose state does not match lands there with
/// <c>sign_in_failed</c>, a refusal at the provider lands with <c>access_denied</c>, and a configured landing that
/// the landing rule refuses (WEB-0080) refuses the start; on this Kestrel host a challenge landing that could not be a
/// header is refused with 400 and an accepted one reaches the browser. The dev Test provider (oauth2, self-hosted)
/// is the provider.
/// </summary>
public sealed class FailureLandingSpec
{
    [Fact]
    public async Task A_remote_failure_and_a_refusal_land_on_the_configured_path_with_their_codes()
    {
        await using var app = await Start(new() { ["Koan:Web:Auth:ReturnUrl:FailurePath"] = "/signin" });
        using var client = app.NewClient();

        var mismatch = await client.GetAsync("/auth/test/callback?state=bogus&code=x", TestContext.Current.CancellationToken);
        mismatch.StatusCode.Should().Be(HttpStatusCode.Redirect);
        mismatch.Headers.Location!.ToString().Should().Be("/signin?error=sign_in_failed");
        mismatch.Headers.CacheControl!.ToString().Should().Be("no-store");

        var challenge = await client.GetAsync("/auth/test/challenge?return=/", TestContext.Current.CancellationToken);
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = challenge.Headers.Location!.OriginalString;
        var state = QueryHelpers.ParseQuery(location[location.IndexOf('?')..])["state"].ToString();
        state.Should().NotBeNullOrEmpty("the challenge protected its state for the provider");

        var refused = await client.GetAsync($"/auth/test/callback?state={Uri.EscapeDataString(state)}&error=access_denied", TestContext.Current.CancellationToken);
        refused.StatusCode.Should().Be(HttpStatusCode.Redirect);
        refused.Headers.Location!.ToString().Should().Be("/signin?error=access_denied");
    }

    [Fact]
    public async Task Without_a_landing_the_handler_answers_as_before()
    {
        await using var app = await Start(new());
        using var client = app.NewClient();

        var mismatch = await client.GetAsync("/auth/test/callback?state=bogus&code=x", TestContext.Current.CancellationToken);
        mismatch.StatusCode.Should().NotBe(HttpStatusCode.Redirect, "nothing is named, so the maintained handler's own failure stands");
    }

    [Theory]
    [InlineData("https://evil.example/signin")]
    [InlineData("/../admin")]
    [InlineData("/%2e%2e/admin")]
    [InlineData("/a%2Fb")]
    [InlineData("/é")]
    [InlineData("/%C3%A9")]
    [InlineData("/signin?error=forged")]
    public async Task A_configured_landing_the_rule_refuses_refuses_the_start(string landing)
    {
        var start = () => Start(new() { ["Koan:Web:Auth:ReturnUrl:FailurePath"] = landing });
        (await start.Should().ThrowAsync<Exception>()).Which.ToString()
            .Should().Contain("Koan:Web:Auth:ReturnUrl:FailurePath").And.Contain("rooted path");
    }

    /// <summary>
    /// Kestrel refuses a response header outside printable ASCII by throwing, which TestServer does not; so on this
    /// real Kestrel host a landing that could not be a header answers 400 at the challenge instead of a 500 at the
    /// failure, and one that is accepted reaches the browser as its <c>Location</c>.
    /// </summary>
    [Theory]
    [InlineData("/é")]
    [InlineData("/\u2215admin")]
    [InlineData("/%C3%A9")]
    public async Task A_challenge_landing_that_could_not_be_a_header_is_refused_with_400(string landing)
    {
        await using var app = await Start(new());
        using var client = app.NewClient();

        var response = await client.GetAsync($"/auth/test/challenge?return=/&failure={Uri.EscapeDataString(landing)}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("'failure'");
    }

    [Fact]
    public async Task An_accepted_challenge_landing_reaches_the_browser_as_its_location()
    {
        await using var app = await Start(new());
        using var client = app.NewClient();

        var challenge = await client.GetAsync($"/auth/test/challenge?return=/&failure={Uri.EscapeDataString("/signin?from=a%20b")}", TestContext.Current.CancellationToken);
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = challenge.Headers.Location!.OriginalString;
        var state = QueryHelpers.ParseQuery(location[location.IndexOf('?')..])["state"].ToString();

        var refused = await client.GetAsync($"/auth/test/callback?state={Uri.EscapeDataString(state)}&error=access_denied", TestContext.Current.CancellationToken);
        refused.StatusCode.Should().Be(HttpStatusCode.Redirect);
        refused.Headers.Location!.OriginalString.Should().Be("/signin?from=a%20b&error=access_denied");
    }

    private static async Task<App> Start(Dictionary<string, string?> settings)
    {
        var port = GrabFreePort();
        var baseUrl = $"http://127.0.0.1:{port}";
        var configuration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "localhost:0",
            ["Logging:LogLevel:Default"] = "Warning",
        };
        foreach (var (key, value) in settings) configuration[key] = value;

        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(configuration))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(baseUrl);
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
        return new App(host, baseUrl);
    }

    private static int GrabFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class App(IHost host, string baseUrl) : IAsyncDisposable
    {
        /// <summary>A cookie-aware, non-auto-redirecting client, so the correlation cookie returns on the callback.</summary>
        public HttpClient NewClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() })
        { BaseAddress = new Uri(baseUrl) };

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
