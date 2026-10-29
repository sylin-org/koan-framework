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
/// SEC-0027 — the failure landing follows the one path rule: under a base URL whose path is <c>/me</c>, a refusal at the
/// provider lands <c>failure=/sign-in</c> on <c>/me/sign-in</c> and <c>failure=/me/sign-in</c> on <c>/me/me/sign-in</c>,
/// application-relative with the configured base exactly once.
/// </summary>
public sealed class FailureLandingUnderPathSpec
{
    [Theory]
    [InlineData("/sign-in", "/me/sign-in?error=access_denied")]
    [InlineData("/me/sign-in", "/me/me/sign-in?error=access_denied")]
    public async Task A_failure_landing_gets_the_base_once(string failure, string expected)
    {
        var port = GrabFreePort();
        var listen = $"http://127.0.0.1:{port}";
        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:0",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Koan:Web:ApplicationUrl"] = listen + "/me",
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
                });
                web.Configure(_ => { });
            })
            .Build();
        await host.StartAsync(Ct);
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() })
            { BaseAddress = new Uri(listen) };

            using var challenge = await client.GetAsync($"/me/auth/test/challenge?return=/me/&failure={Uri.EscapeDataString(failure)}", Ct);
            challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
            var location = challenge.Headers.Location!.OriginalString;
            var state = QueryHelpers.ParseQuery(location[location.IndexOf('?')..])["state"].ToString();

            using var refused = await client.GetAsync($"/me/auth/test/callback?state={Uri.EscapeDataString(state)}&error=access_denied", Ct);

            refused.StatusCode.Should().Be(HttpStatusCode.Redirect);
            refused.Headers.Location!.OriginalString.Should().Be(expected);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int GrabFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
