using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Koan.Web.Auth.Domain;
using Xunit;

namespace Koan.Web.Auth.Integration.Tests;

/// <summary>
/// WEB-0071 / E5 — the ARCH-0079 spec that would have caught OIDC-501. Drives a REAL challenge→callback
/// round-trip for BOTH the oauth2 <c>test</c> provider and the oidc <c>test-oidc</c> provider through the
/// maintained ASP.NET handlers seeded by <c>AuthSchemeSeeder</c>, and asserts: the user lands authenticated,
/// the sub→NameIdentifier mapping fired, the <c>UserInfoMapper</c> role mapping fired (parity), and the
/// external-identity link was recorded (parity). The OIDC case additionally exercises discovery + JWKS +
/// signed-id_token validation + PKCE + nonce inside the handler — none of which the old 501 callback did.
/// </summary>
public sealed class AuthEngineSwapSpec : IClassFixture<AuthSwapFixture>
{
    private readonly AuthSwapFixture _fx;
    public AuthEngineSwapSpec(AuthSwapFixture fx) => _fx = fx;

    [Fact]
    public async Task OAuth2_challenge_callback_round_trip_authenticates_with_mapped_role_and_link()
    {
        using var client = _fx.NewClient();

        await DriveLogin(client, "/auth/test/challenge?return=/e2e/whoami", injectRole: "editor");

        var who = await ReadWhoAmI(client);
        who.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        who.GetProperty("id").GetString().Should().Be("alice@example.com");
        who.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).Should().Contain("editor");

        // Parity: the external-identity link must have been recorded by the OnCreatingTicket hook.
        await AssertLinked("alice@example.com", "test");
    }

    [Fact]
    public async Task OIDC_challenge_callback_round_trip_validates_id_token_and_authenticates()
    {
        using var client = _fx.NewClient();

        // The whole OIDC machinery runs here: metadata discovery, JWKS fetch, PKCE, nonce, and signed
        // id_token validation. If any of it failed the handler would NOT sign in → authenticated=false.
        await DriveLogin(client, "/auth/test-oidc/challenge?return=/e2e/whoami", injectRole: "admin");

        var who = await ReadWhoAmI(client);
        who.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        who.GetProperty("id").GetString().Should().Be("alice@example.com");
        who.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).Should().Contain("admin");

        await AssertLinked("alice@example.com", "test-oidc");
    }

    [Fact]
    public async Task Development_maps_test_endpoints_without_opt_in()
    {
        // Bug-1 regression: the contributor advertises test/test-oidc in Development, so their endpoints MUST be
        // mapped there too — without any TestProvider:Enabled opt-in (the fixture sets none). If advertisement and
        // mapping drift again, the discovery document 404s and OIDC discovery (and the round-trips above) break.
        using var client = _fx.NewClient();
        var resp = await client.GetAsync("/.testoauth/.well-known/openid-configuration");
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "the Test simulator endpoints must auto-map in Development (advertise ⇒ map) with no opt-in");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Manually follows the challenge→authorize→callback→landing 302 chain (the TestServer client doesn't
    /// auto-redirect; cookies — correlation, nonce, auth — flow via the CookieContainer). On the dev Test
    /// authorize hop it appends a <c>roles</c> hint (test-only; in real OAuth custom params aren't forwarded).
    /// </summary>
    private async Task DriveLogin(HttpClient client, string challenge, string injectRole)
    {
        var url = new Uri(_fx.BaseUrl + challenge);
        var chain = new System.Collections.Generic.List<string>();
        for (var hop = 0; hop < 12; hop++)
        {
            if (url.AbsolutePath.EndsWith("/authorize", StringComparison.Ordinal))
                url = new Uri(QueryHelpers.AddQueryString(url.ToString(), "roles", injectRole));

            using var resp = await client.GetAsync(url);
            chain.Add($"{(int)resp.StatusCode} {url.PathAndQuery}");
            if ((int)resp.StatusCode is < 300 or >= 400)
            {
                var body = (int)resp.StatusCode >= 400 ? await resp.Content.ReadAsStringAsync() : "";
                resp.StatusCode.Should().Be(HttpStatusCode.OK,
                    $"the round-trip should land on the return URL. chain:\n  {string.Join("\n  ", chain)}\nbody: {body[..Math.Min(body.Length, 800)]}");
                return;
            }
            var loc = resp.Headers.Location ?? throw new InvalidOperationException($"redirect with no Location at {url}. chain: {string.Join(" -> ", chain)}");
            url = loc.IsAbsoluteUri ? loc : new Uri(url, loc);
        }
        throw new InvalidOperationException("redirect chain did not terminate within 12 hops: " + string.Join(" -> ", chain));
    }

    private static async Task<JsonElement> ReadWhoAmI(HttpClient client)
    {
        var resp = await client.GetAsync("/e2e/whoami");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private async Task AssertLinked(string userId, string provider)
    {
        var store = _fx.Services.GetRequiredService<IExternalIdentityStore>();
        var links = await store.GetByUser(userId);
        links.Select(l => l.Provider).Should().Contain(provider);
    }
}
