using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace Koan.Security.Trust.IntegrationTests;

/// <summary>
/// HTTP-level end-to-end coverage of the SEC-0001/SEC-0002 fabric over the real pipeline: the inbound bearer
/// scheme (KSVID validation), the zero-config dev identity, and a bare [Authorize] gate.
/// </summary>
public sealed class AuthEndToEndSpec : IClassFixture<AuthE2EFixture>
{
    private readonly AuthE2EFixture _fx;
    public AuthEndToEndSpec(AuthE2EFixture fx) => _fx = fx;

    [Fact]
    public async Task Open_endpoint_serves_over_the_real_pipeline()
    {
        var response = await _fx.CreateClient().GetAsync("/e2e/open");
        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task Endpoint_contributor_is_mapped_in_the_pipeline()
    {
        // WEB-0069: an IKoanEndpointContributor maps inside Koan's UseEndpoints block (the path MCP uses).
        var response = await _fx.CreateClient().GetAsync("/e2e/contributed");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── inbound bearer (Koan.bearer) ────────────────────────────────────

    [Fact]
    public async Task Bearer_endpoint_accepts_a_KSVID_minted_by_the_app_issuer()
    {
        var token = _fx.MintBearer("svc-1", "admin");
        var req = new HttpRequestMessage(HttpMethod.Get, "/e2e/bearer");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await _fx.CreateClient().SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJson(res)).GetProperty("sub").GetString().Should().Be("svc-1");
    }

    [Fact]
    public async Task Bearer_endpoint_rejects_a_missing_token_with_401()
    {
        var res = await _fx.CreateClient().GetAsync("/e2e/bearer");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Bearer_endpoint_rejects_a_garbage_token_with_401()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/e2e/bearer");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        var res = await _fx.CreateClient().SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── zero-config dev identity ────────────────────────────────────────

    [Fact]
    public async Task Default_is_anonymous_no_auto_signin()
    {
        // SEC-0003 §2.1 — a fresh request is unauthenticated (public), not auto-signed-in.
        var json = await ReadJson(await _fx.CreateClient().GetAsync("/e2e/whoami"));
        json.GetProperty("authenticated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Dev_identity_can_be_opted_out_per_request_with_as_anonymous()
    {
        var json = await ReadJson(await _fx.CreateClient().GetAsync("/e2e/whoami?_as=anonymous"));
        json.GetProperty("authenticated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Dev_identity_supports_impersonation_via_as_and_roles()
    {
        var json = await ReadJson(await _fx.CreateClient().GetAsync("/e2e/whoami?_as=alice&_roles=editor"));
        json.GetProperty("id").GetString().Should().Be("alice");
        json.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).Should().Contain("editor");
    }

    // ── bare [Authorize] (cookie/default scheme) ────────────────────────

    [Fact]
    public async Task Bare_authorize_passes_when_impersonating()
    {
        var res = await _fx.CreateClient().GetAsync("/e2e/cookie?_as=alice");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Bare_authorize_is_denied_by_default()
    {
        // No ?_as ⇒ anonymous ⇒ [Authorize] denies (the public-first default).
        var res = await _fx.CreateClient().GetAsync("/e2e/cookie");
        res.IsSuccessStatusCode.Should().BeFalse();
    }

    // ── role gate (RBAC over dev-identity roles) ────────────────────────

    [Fact]
    public async Task Role_gate_allows_an_impersonated_admin()
    {
        var res = await _fx.CreateClient().GetAsync("/e2e/admin?_as=alice&_roles=admin");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Role_gate_returns_403_for_an_authenticated_non_admin_api_request()
    {
        // An API-shaped request (Accept: application/json) gets a real 403 via the built-in content-negotiated
        // JsonChallengeHandler — not the browser access-denied redirect. (Confirms the cookie scheme is BFF-correct.)
        var req = new HttpRequestMessage(HttpMethod.Get, "/e2e/admin?_as=bob&_roles=viewer");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var res = await _fx.CreateClient().SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
