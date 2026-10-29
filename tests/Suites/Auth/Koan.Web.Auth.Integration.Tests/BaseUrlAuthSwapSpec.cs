using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Koan.Web.Auth.Integration.Tests;

/// <summary>
/// WEB-0078 — the Development Test IdP under the application's base URL: its discovery issues under the path with
/// endpoints under the path, nothing answers at the root, and both the oauth2 and the oidc challenge→callback round
/// trips land authenticated when every hop runs under <c>/tangent</c>.
/// </summary>
public sealed class BaseUrlAuthSwapSpec : IClassFixture<BaseUrlAuthSwapFixture>
{
    private readonly BaseUrlAuthSwapFixture _fx;
    public BaseUrlAuthSwapSpec(BaseUrlAuthSwapFixture fx) => _fx = fx;

    [Fact]
    public async Task Discovery_issues_under_the_base_url_and_not_at_the_root()
    {
        using var client = _fx.NewClient();

        using var discovery = await client.GetAsync($"{BaseUrlAuthSwapFixture.PathBase}/.testoauth/.well-known/openid-configuration");
        discovery.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("issuer").GetString().Should().Be($"{_fx.ApplicationUrl}/.testoauth");
        document.RootElement.GetProperty("authorization_endpoint").GetString().Should().Be($"{_fx.ApplicationUrl}/.testoauth/authorize");
        document.RootElement.GetProperty("token_endpoint").GetString().Should().Be($"{_fx.ApplicationUrl}/.testoauth/token");
        document.RootElement.GetProperty("jwks_uri").GetString().Should().Be($"{_fx.ApplicationUrl}/.testoauth/jwks");

        (await client.GetAsync("/.testoauth/.well-known/openid-configuration")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/.testoauth/login.html")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"{BaseUrlAuthSwapFixture.PathBase}/.testoauth/login.html")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OAuth2_round_trip_under_the_path_authenticates()
    {
        using var client = _fx.NewClient();

        await DriveLogin(client, $"{BaseUrlAuthSwapFixture.PathBase}/auth/test/challenge?return={BaseUrlAuthSwapFixture.PathBase}/e2e/whoami", "editor");

        var who = await ReadWhoAmI(client);
        who.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        who.GetProperty("id").GetString().Should().Be("alice@example.com");
    }

    [Fact]
    public async Task OIDC_round_trip_under_the_path_validates_the_id_token_and_authenticates()
    {
        using var client = _fx.NewClient();

        await DriveLogin(client, $"{BaseUrlAuthSwapFixture.PathBase}/auth/test-oidc/challenge?return={BaseUrlAuthSwapFixture.PathBase}/e2e/whoami", "admin");

        var who = await ReadWhoAmI(client);
        who.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        who.GetProperty("id").GetString().Should().Be("alice@example.com");
    }

    /// <summary>Follows the challenge→authorize→callback→landing 302 chain; every hop must stay under the path.</summary>
    private async Task DriveLogin(HttpClient client, string challenge, string injectRole)
    {
        var url = new Uri(_fx.BaseUrl + challenge);
        var chain = new List<string>();
        for (var hop = 0; hop < 12; hop++)
        {
            if (url.AbsolutePath.EndsWith("/authorize", StringComparison.Ordinal))
                url = new Uri(QueryHelpers.AddQueryString(url.ToString(), "roles", injectRole));

            url.AbsolutePath.Should().StartWith(BaseUrlAuthSwapFixture.PathBase + "/",
                $"no hop may leave the path base. chain: {string.Join(" -> ", chain)}");

            using var resp = await client.GetAsync(url, TestContext.Current.CancellationToken);
            chain.Add($"{(int)resp.StatusCode} {url.PathAndQuery}");
            if ((int)resp.StatusCode is < 300 or >= 400)
            {
                var body = (int)resp.StatusCode >= 400
                    ? await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
                    : "";
                resp.StatusCode.Should().Be(HttpStatusCode.OK,
                    $"the round-trip should land on the return URL. chain:\n  {string.Join("\n  ", chain)}\nbody: {body[..Math.Min(body.Length, 800)]}");
                return;
            }

            var location = resp.Headers.Location ?? throw new InvalidOperationException(
                $"redirect with no Location at {url}. chain: {string.Join(" -> ", chain)}");
            url = location.IsAbsoluteUri ? location : new Uri(url, location);
        }
        throw new InvalidOperationException("redirect chain did not terminate within 12 hops: " + string.Join(" -> ", chain));
    }

    private static async Task<JsonElement> ReadWhoAmI(HttpClient client)
    {
        var resp = await client.GetAsync($"{BaseUrlAuthSwapFixture.PathBase}/e2e/whoami");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
