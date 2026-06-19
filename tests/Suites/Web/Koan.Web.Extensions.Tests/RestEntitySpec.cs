using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// ARCH-0092 (Phase 2, §B) — the terse REST exposure path. A bare <c>[RestEntity]</c> on an entity
/// (no hand-written controller) auto-registers a full-CRUD <c>EntityController</c> over the same governed
/// <c>IEntityEndpointService</c> as the explicit path, routed by convention. An explicit controller wins.
/// </summary>
[Collection(RestEntityCollection.Name)]
public sealed class RestEntitySpec
{
    private readonly RestEntityWebFactory _fx;

    public RestEntitySpec(RestEntityWebFactory fx) => _fx = fx;

    private HttpClient Client => _fx.Client;

    [Fact]
    public async Task Terse_RestEntity_exposes_full_CRUD_at_the_derived_route()
    {
        // CREATE — POST to the derived route api/trinket
        var create = await Client.PostAsJsonAsync("/api/trinket", new { id = "t-1", name = "Alpha" });
        create.IsSuccessStatusCode.Should().BeTrue($"POST should create; body: {await create.Content.ReadAsStringAsync()}");

        // READ one — GET by id
        var byId = await Client.GetAsync("/api/trinket/t-1");
        byId.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await byId.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("id").GetString().Should().Be("t-1");
        doc.GetProperty("name").GetString().Should().Be("Alpha");

        // READ collection — GET list contains the created item
        var list = await Client.GetAsync("/api/trinket");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await ReadItems(list);
        items.Select(IdOf).Should().Contain("t-1", "the terse path runs the same governed endpoint service");

        // DELETE — and confirm gone
        var del = await Client.DeleteAsync("/api/trinket/t-1");
        del.IsSuccessStatusCode.Should().BeTrue();
        (await Client.GetAsync("/api/trinket/t-1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Terse_RestEntity_honors_an_explicit_route_override()
    {
        var create = await Client.PostAsJsonAsync("/api/gizmos", new { id = "g-1", label = "Bravo" });
        create.IsSuccessStatusCode.Should().BeTrue();

        (await Client.GetAsync("/api/gizmos/g-1")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The kebab default (api/gizmo) must NOT exist — the override replaced it.
        (await Client.GetAsync("/api/gizmo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Explicit_controller_wins_over_the_terse_attribute()
    {
        // Cog has [RestEntity] AND CogController([Route("api/cogs")]). Explicit wins (§B).
        var create = await Client.PostAsJsonAsync("/api/cogs", new { id = "c-1", teeth = "12" });
        create.IsSuccessStatusCode.Should().BeTrue();
        (await Client.GetAsync("/api/cogs/c-1")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The terse default route must NOT be registered — no double exposure / collision.
        (await Client.GetAsync("/api/cog")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static string? IdOf(JsonElement e)
        => e.TryGetProperty("id", out var i) ? i.GetString() : null;

    private static async Task<List<JsonElement>> ReadItems(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw)) return new List<JsonElement>();

        var root = JsonDocument.Parse(raw).RootElement.Clone();
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().ToList();
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            return items.EnumerateArray().ToList();
        throw new InvalidOperationException($"Unexpected response shape: {raw[..Math.Min(200, raw.Length)]}");
    }
}
