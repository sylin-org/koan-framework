using System.Net;
using System.Net.Http;
using System.Text;
using AwesomeAssertions;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// ARCH-0092 (Phase 3.2, §D) — the entity floor enforced end-to-end over REST. A <c>[RequireScope]</c> on the
/// entity is honored by the unified seam inside the shared <c>EntityEndpointService</c>: anonymous → 401,
/// wrong scope → 403, right scope → 200. The same gate runs for the MCP edge (Phase 3.3), so the declaration
/// is enforced identically on every surface.
/// </summary>
[Collection(RestEntityCollection.Name)]
public sealed class EntityFloorE2ESpec
{
    private readonly RestEntityWebFactory _fx;

    public EntityFloorE2ESpec(RestEntityWebFactory fx) => _fx = fx;

    private HttpRequestMessage Get(string path, string? scopes = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (scopes is not null) req.Headers.Add("X-Test-Scopes", scopes);
        return req;
    }

    [Fact]
    public async Task Anonymous_request_to_a_scoped_entity_is_challenged_401()
    {
        var res = await _fx.Client.SendAsync(Get("/api/vault"));
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "[RequireScope] + unauthenticated → Challenge → 401");
    }

    [Fact]
    public async Task Authenticated_without_the_required_scope_is_forbidden_403()
    {
        var res = await _fx.Client.SendAsync(Get("/api/vault", scopes: "vault:write other:read"));
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden, "authenticated but missing vault:read → Forbid → 403");
    }

    [Fact]
    public async Task Authenticated_with_the_required_scope_is_allowed_200()
    {
        var res = await _fx.Client.SendAsync(Get("/api/vault", scopes: "vault:read"));
        res.StatusCode.Should().Be(HttpStatusCode.OK, "the declared scope is held → seam allows");
    }

    [Fact]
    public async Task An_ungated_entity_stays_open_no_regression()
    {
        // Trinket carries [RestEntity] but no floor attribute → seam defers → allow-by-default.
        var res = await _fx.Client.SendAsync(Get("/api/trinket"));
        res.StatusCode.Should().Be(HttpStatusCode.OK, "no floor declared → CRUD stays open");
    }

    // ── SEC-0004 per-action gate: read and write require DIFFERENT scopes (Strongbox) ────────────────────────────
    // The entity-wide [RequireScope] floor structurally cannot express this; the per-action gate can.

    [Fact]
    public async Task Read_is_gated_on_the_read_scope_only()
    {
        var ok = await _fx.Client.SendAsync(Get("/api/strongbox", scopes: "strongbox:read"));
        ok.StatusCode.Should().Be(HttpStatusCode.OK, "read gate is has:scope:strongbox:read");

        var wrong = await _fx.Client.SendAsync(Get("/api/strongbox", scopes: "strongbox:write"));
        wrong.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the WRITE scope does not satisfy the READ gate — per-action granularity");

        var anon = await _fx.Client.SendAsync(Get("/api/strongbox"));
        anon.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Write_is_gated_on_the_write_scope_independently_of_read()
    {
        var ok = await _fx.Client.SendAsync(Post("/api/strongbox", "{\"contents\":\"x\"}", scopes: "strongbox:write"));
        ok.IsSuccessStatusCode.Should().BeTrue("write gate is has:scope:strongbox:write");

        var wrong = await _fx.Client.SendAsync(Post("/api/strongbox", "{\"contents\":\"x\"}", scopes: "strongbox:read"));
        wrong.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the READ scope does not satisfy the WRITE gate");
    }

    // ── SEC-0004 projection: the single Koan-Access list header (replaces the 3 booleans) ────────────────────────

    [Fact]
    public async Task An_open_entity_advertises_every_verb()
    {
        var res = await _fx.Client.SendAsync(Get("/api/trinket"));
        Access(res).Should().Be("read, write, remove");
        res.Headers.Contains("Koan-Access-Read").Should().BeFalse("the three boolean headers are collapsed into one list header");
    }

    [Fact]
    public async Task A_non_admin_does_not_see_the_remove_verb()
    {
        // Ledger: read + write open, remove is:admin. Anonymous principal → read, write (no remove).
        var res = await _fx.Client.SendAsync(Get("/api/ledger"));
        Access(res).Should().Be("read, write");
    }

    [Fact]
    public async Task An_admin_sees_the_remove_verb()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/ledger");
        req.Headers.Add("X-Test-Roles", "admin");
        var res = await _fx.Client.SendAsync(req);
        Access(res).Should().Be("read, write, remove");
    }

    private static string Access(HttpResponseMessage res)
        => res.Headers.TryGetValues("Koan-Access", out var v) ? string.Join(", ", v) : "<absent>";

    private static HttpRequestMessage Post(string path, string json, string? scopes = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (scopes is not null) req.Headers.Add("X-Test-Scopes", scopes);
        return req;
    }
}
