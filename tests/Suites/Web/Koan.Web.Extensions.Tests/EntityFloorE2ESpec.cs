using System.Net;
using System.Net.Http.Headers;
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
}
