using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Xunit;

namespace Koan.Web.AdapterSurface.TestKit;

/// <summary>
/// Comprehensive EntityController surface specs that exercise every endpoint and every
/// sort/page scenario. Subclassed per Koan data adapter — the subclass provides the
/// <typeparamref name="TFactory"/> binding via xUnit's IClassFixture pattern.
///
/// Adapters that need infrastructure (Docker, etc.) gate via <see cref="SkippableFactAttribute"/>;
/// hosts without Docker get green-skipped tests.
/// </summary>
public abstract class AdapterSurfaceSpecsBase<TFactory> : IClassFixture<TFactory>, IAsyncLifetime
    where TFactory : class, IAdapterTestFactory
{
    protected readonly TFactory Factory;
    protected HttpClient Client => Factory.Client;

    protected AdapterSurfaceSpecsBase(TFactory factory)
    {
        Factory = factory;
    }

    public async Task InitializeAsync()
    {
        if (!Factory.IsAvailable) return;
        AppHost.Current = Factory.Services;
        await Factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    protected void SkipIfUnavailable()
        => Skip.If(!Factory.IsAvailable, $"[{typeof(TFactory).Name}] {Factory.UnavailableReason ?? "Adapter infrastructure unavailable"}");

    // ============================================================================================
    // GET /api/widgets — collection list
    // ============================================================================================

    [SkippableFact]
    public async Task GetCollection_empty_returns_empty_array()
    {
        SkipIfUnavailable();

        var response = await Client.GetAsync("/api/widgets");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task GetCollection_after_upserts_returns_all_items()
    {
        SkipIfUnavailable();
        await SeedAlphaBravoCharlie();

        var response = await Client.GetAsync("/api/widgets");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Should().HaveCount(3);
    }

    // === Sort grammar (DATA-0092) ===

    [SkippableFact]
    public async Task GetCollection_sort_minus_prefix_descends()
    {
        SkipIfUnavailable();
        await SeedAlphaBravoCharlie();

        var response = await Client.GetAsync("/api/widgets?sort=-Name");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(NameOf).Should().Equal("Charlie", "Bravo", "Alpha");
    }

    [SkippableFact]
    public async Task GetCollection_sort_plus_prefix_ascends()
    {
        SkipIfUnavailable();
        await SeedAlphaBravoCharlie();

        // %2B = '+' URL-encoded. Pre-DATA-0092 this was silently dropped.
        var response = await Client.GetAsync("/api/widgets?sort=%2BName");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(NameOf).Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [SkippableFact]
    public async Task GetCollection_sort_no_prefix_ascends()
    {
        SkipIfUnavailable();
        await SeedAlphaBravoCharlie();

        var response = await Client.GetAsync("/api/widgets?sort=Name");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(NameOf).Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [SkippableFact]
    public async Task GetCollection_multi_field_sort_applies_in_order()
    {
        SkipIfUnavailable();
        await UpsertWidget("1", name: "Bravo", priority: 1);
        await UpsertWidget("2", name: "Alpha", priority: 3);
        await UpsertWidget("3", name: "Bravo", priority: 5);

        var response = await Client.GetAsync("/api/widgets?sort=Name,-Priority");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Should().HaveCount(3);
        NameOf(items[0]).Should().Be("Alpha");
        NameOf(items[1]).Should().Be("Bravo");
        items[1].GetProperty("priority").GetInt32().Should().BeGreaterThan(items[2].GetProperty("priority").GetInt32());
    }

    // === The original bug: deep-path collection sort ===

    [SkippableFact]
    public async Task GetCollection_deep_path_sort_aggregates_max_for_desc()
    {
        SkipIfUnavailable();
        await SeedWidgetsWithSightings();

        var response = await Client.GetAsync("/api/widgets?sort=-Sightings.LastChangedAt");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        // Expected: d (2027) > b (2026) > c (2025-06) > a (2024-06) > e (2023)
        items.Select(IdOf).Should().Equal("d", "b", "c", "a", "e");
    }

    [SkippableFact]
    public async Task GetCollection_deep_path_sort_and_pagination_returns_correct_window()
    {
        SkipIfUnavailable();
        await SeedWidgetsWithSightings();

        var page1 = await Client.GetAsync("/api/widgets?sort=-Sightings.LastChangedAt&page=1&pageSize=2");
        page1.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadItems(page1)).Select(IdOf).Should().Equal("d", "b");

        var page2 = await Client.GetAsync("/api/widgets?sort=-Sightings.LastChangedAt&page=2&pageSize=2");
        page2.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadItems(page2)).Select(IdOf).Should().Equal("c", "a");

        var page3 = await Client.GetAsync("/api/widgets?sort=-Sightings.LastChangedAt&page=3&pageSize=2");
        page3.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadItems(page3)).Select(IdOf).Should().Equal("e");
    }

    [SkippableFact]
    public async Task GetCollection_unresolvable_sort_field_returns_400()
    {
        SkipIfUnavailable();
        await SeedAlphaBravoCharlie();

        var response = await Client.GetAsync("/api/widgets?sort=NonexistentField");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("field").GetString().Should().Be("NonexistentField");
    }

    // === Pagination + sort-after-paginate regression ===

    [SkippableFact]
    public async Task GetCollection_page_one_sorted_asc_returns_globally_smallest_not_first_inserted()
    {
        SkipIfUnavailable();
        // Insert in reverse alpha so natural order != sorted order.
        for (var i = 9; i >= 0; i--)
        {
            await UpsertWidget($"w{i:D2}", name: $"W{(char)('A' + i)}");
        }

        var response = await Client.GetAsync("/api/widgets?sort=Name&page=1&pageSize=3");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Should().HaveCount(3);
        items.Select(NameOf).Should().Equal("WA", "WB", "WC");
    }

    [SkippableFact]
    public async Task GetCollection_pagination_headers_present()
    {
        SkipIfUnavailable();
        for (var i = 0; i < 10; i++)
        {
            await UpsertWidget($"w{i:D2}", name: $"W{(char)('A' + i)}");
        }

        var response = await Client.GetAsync("/api/widgets?sort=Name&page=2&pageSize=3");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Page").Should().ContainSingle().Which.Should().Be("2");
        response.Headers.GetValues("X-Page-Size").Should().ContainSingle().Which.Should().Be("3");
    }

    // ============================================================================================
    // GET /api/widgets/{id}
    // ============================================================================================

    [SkippableFact]
    public async Task GetById_returns_existing_entity()
    {
        SkipIfUnavailable();
        await UpsertWidget("the-id", name: "Hello");

        var response = await Client.GetAsync("/api/widgets/the-id");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("id").GetString().Should().Be("the-id");
        doc.GetProperty("name").GetString().Should().Be("Hello");
    }

    [SkippableFact]
    public async Task GetById_missing_returns_404()
    {
        SkipIfUnavailable();

        var response = await Client.GetAsync("/api/widgets/missing-id");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ============================================================================================
    // POST /api/widgets — upsert
    // ============================================================================================

    [SkippableFact]
    public async Task Upsert_creates_new_entity_and_returns_it()
    {
        SkipIfUnavailable();

        var response = await Client.PostAsJsonAsync("/api/widgets", new
        {
            id = "post-1",
            name = "Created",
            priority = 7
        });
        ((int)response.StatusCode).Should().BeOneOf(200, 201);

        var got = await Client.GetAsync("/api/widgets/post-1");
        got.StatusCode.Should().Be(HttpStatusCode.OK);
        (await got.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().Should().Be("Created");
    }

    [SkippableFact]
    public async Task Upsert_updates_existing_entity()
    {
        SkipIfUnavailable();
        await UpsertWidget("update-target", name: "Before");

        await Client.PostAsJsonAsync("/api/widgets", new { id = "update-target", name = "After", priority = 99 });

        var got = await Client.GetAsync("/api/widgets/update-target");
        var doc = await got.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("name").GetString().Should().Be("After");
        doc.GetProperty("priority").GetInt32().Should().Be(99);
    }

    // ============================================================================================
    // POST /api/widgets/bulk — upsertMany
    // ============================================================================================

    [SkippableFact]
    public async Task UpsertMany_creates_multiple_entities()
    {
        SkipIfUnavailable();

        var response = await Client.PostAsJsonAsync("/api/widgets/bulk", new[]
        {
            new { id = "bulk-a", name = "BulkA" },
            new { id = "bulk-b", name = "BulkB" },
            new { id = "bulk-c", name = "BulkC" }
        });
        ((int)response.StatusCode).Should().BeOneOf(200, 201);

        var list = await ReadItems(await Client.GetAsync("/api/widgets"));
        list.Select(IdOf).Should().Contain(new[] { "bulk-a", "bulk-b", "bulk-c" });
    }

    // ============================================================================================
    // DELETE /api/widgets/{id}
    // ============================================================================================

    [SkippableFact]
    public async Task Delete_by_id_removes_entity()
    {
        SkipIfUnavailable();
        await UpsertWidget("delete-me", name: "Doomed");

        var del = await Client.DeleteAsync("/api/widgets/delete-me");
        ((int)del.StatusCode).Should().BeOneOf(200, 204);

        var got = await Client.GetAsync("/api/widgets/delete-me");
        got.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ============================================================================================
    // POST /api/widgets/query — body-query (filter + sort + page)
    // ============================================================================================

    [SkippableFact]
    public async Task BodyQuery_with_sort_array_applies_sort()
    {
        SkipIfUnavailable();
        await SeedAlphaBravoCharlie();

        var response = await Client.PostAsJsonAsync("/api/widgets/query", new
        {
            sort = new[] { "-Name" }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(NameOf).Should().Equal("Charlie", "Bravo", "Alpha");
    }

    [SkippableFact]
    public async Task BodyQuery_with_deep_path_sort_and_pagination()
    {
        SkipIfUnavailable();
        await SeedWidgetsWithSightings();

        var response = await Client.PostAsJsonAsync("/api/widgets/query", new
        {
            sort = new[] { "-Sightings.LastChangedAt" },
            page = 1,
            size = 2
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(IdOf).Should().Equal("d", "b");
    }

    [SkippableFact]
    public async Task BodyQuery_with_unresolvable_sort_returns_400()
    {
        SkipIfUnavailable();
        await SeedAlphaBravoCharlie();

        var response = await Client.PostAsJsonAsync("/api/widgets/query", new
        {
            sort = new[] { "DoesNotExist" }
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ============================================================================================
    // Sanity: full round-trip with deep-path collection (the original bug, end-to-end)
    // ============================================================================================

    [SkippableFact]
    public async Task FullRoundTrip_deep_path_sort_pagination_matches_expected_order_for_every_page()
    {
        SkipIfUnavailable();
        await SeedWidgetsWithSightings();

        // Spot-check each page against the globally-sorted expected sequence.
        var expected = new[] { "d", "b", "c", "a", "e" };
        for (var page = 1; page <= 3; page++)
        {
            var response = await Client.GetAsync($"/api/widgets?sort=-Sightings.LastChangedAt&page={page}&pageSize=2");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var items = await ReadItems(response);
            var window = expected.Skip((page - 1) * 2).Take(2).ToArray();
            items.Select(IdOf).Should().Equal(window);
        }
    }

    // ============================================================================================
    // Helpers
    // ============================================================================================

    private async Task SeedAlphaBravoCharlie()
    {
        await UpsertWidget("id1", name: "Charlie");
        await UpsertWidget("id2", name: "Alpha");
        await UpsertWidget("id3", name: "Bravo");
    }

    private async Task SeedWidgetsWithSightings()
    {
        await UpsertWidget("a", name: "Alpha", sightings:
        [
            new() { Location = "Madrid", LastChangedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            new() { Location = "Paris", LastChangedAt = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero) }
        ]);
        await UpsertWidget("b", name: "Bravo", sightings:
        [
            new() { Location = "Tokyo", LastChangedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }
        ]);
        await UpsertWidget("c", name: "Charlie", sightings:
        [
            new() { Location = "Berlin", LastChangedAt = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero) }
        ]);
        await UpsertWidget("d", name: "Delta", sightings:
        [
            new() { Location = "London", LastChangedAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero) }
        ]);
        await UpsertWidget("e", name: "Echo", sightings:
        [
            new() { Location = "Cairo", LastChangedAt = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero) }
        ]);
    }

    private async Task UpsertWidget(string id, string? name = null, int priority = 0, List<Sighting>? sightings = null)
    {
        AppHost.Current = Factory.Services;
        await Widget.Upsert(new Widget
        {
            Id = id,
            Name = name ?? id,
            Priority = priority,
            Sightings = sightings ?? new List<Sighting>()
        });
    }

    protected static string? NameOf(JsonElement e)
        => e.TryGetProperty("name", out var n) ? n.GetString() : null;

    protected static string? IdOf(JsonElement e)
        => e.TryGetProperty("id", out var i) ? i.GetString() : null;

    protected static async Task<List<JsonElement>> ReadItems(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw)) return new List<JsonElement>();

        var doc = JsonDocument.Parse(raw).RootElement.Clone();
        if (doc.ValueKind == JsonValueKind.Array)
        {
            return doc.EnumerateArray().ToList();
        }
        if (doc.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            return items.EnumerateArray().ToList();
        }
        throw new InvalidOperationException(
            $"Unexpected response shape: {raw[..Math.Min(200, raw.Length)]}");
    }
}
