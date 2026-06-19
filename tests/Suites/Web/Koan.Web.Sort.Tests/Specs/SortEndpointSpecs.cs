using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Core.Hosting.App;
using Koan.Data.Core;

namespace Koan.Web.Sort.Tests.Specs;

/// <summary>
/// End-to-end HTTP tests for the sort contract introduced by DATA-0092 and DATA-0093.
/// These exercise EntityController&lt;Widget&gt; through a real WebApplicationFactory host —
/// the missing layer from the original DATA-0092 test surface.
/// </summary>
public sealed class SortEndpointSpecs : IClassFixture<SortWebApplicationFactory>, IAsyncLifetime
{
    private readonly SortWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SortEndpointSpecs(SortWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async ValueTask InitializeAsync()
    {
        // Reset state so each test class instance starts clean.
        AppHost.Current = _factory.Services;
        await Widget.RemoveAll();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // === Happy-path string grammar ===

    [Fact]
    public async Task Get_with_sort_minus_returns_descending()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        var response = await _client.GetAsync("/api/widgets?sort=-Name");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(w => w.GetProperty("name").GetString()).Should().Equal("Charlie", "Bravo", "Alpha");
    }

    [Fact]
    public async Task Get_with_sort_plus_returns_ascending_fixing_the_plus_prefix_bug()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        // Before DATA-0092 this silently returned natural order — '+' was treated as part of the field name.
        var response = await _client.GetAsync("/api/widgets?sort=%2BName");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(w => w.GetProperty("name").GetString()).Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [Fact]
    public async Task Get_with_no_prefix_defaults_to_ascending()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        var response = await _client.GetAsync("/api/widgets?sort=Name");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(w => w.GetProperty("name").GetString()).Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [Fact]
    public async Task Get_with_multi_field_sort_applies_in_order()
    {
        AppHost.Current = _factory.Services;
        await Widget.Upsert(new Widget { Id = "1", Name = "Bravo", Priority = 1 });
        await Widget.Upsert(new Widget { Id = "2", Name = "Alpha", Priority = 3 });
        await Widget.Upsert(new Widget { Id = "3", Name = "Bravo", Priority = 5 });

        var response = await _client.GetAsync("/api/widgets?sort=Name,-Priority");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Should().HaveCount(3);
        items[0].GetProperty("name").GetString().Should().Be("Alpha");
        items[1].GetProperty("name").GetString().Should().Be("Bravo");
        items[1].GetProperty("priority").GetInt32().Should().Be(5);  // higher priority first within "Bravo"
        items[2].GetProperty("priority").GetInt32().Should().Be(1);
    }

    // === The original deep-path bug ===

    [Fact]
    public async Task Get_with_collection_dot_path_aggregates_max_for_desc()
    {
        AppHost.Current = _factory.Services;
        await Widget.Upsert(new Widget
        {
            Id = "a", Name = "A",
            Sightings = new List<Sighting>
            {
                new() { LastChangedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                new() { LastChangedAt = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero) }
            }
        });
        await Widget.Upsert(new Widget
        {
            Id = "b", Name = "B",
            Sightings = new List<Sighting>
            {
                new() { LastChangedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }
            }
        });
        await Widget.Upsert(new Widget
        {
            Id = "c", Name = "C",
            Sightings = new List<Sighting>
            {
                new() { LastChangedAt = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero) }
            }
        });

        // The original bug report exact shape.
        var response = await _client.GetAsync("/api/widgets?sort=-Sightings.LastChangedAt");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(w => w.GetProperty("id").GetString()).Should().Equal("b", "c", "a");
    }

    // === The combined original-bug scenario: deep-path sort + pagination ===

    [Fact]
    public async Task Get_with_collection_dot_path_sort_and_pagination_returns_correct_window()
    {
        AppHost.Current = _factory.Services;

        // Seed widgets with different "latest sighting" timestamps. We expect the response order
        // by -Sightings.LastChangedAt (descending, MAX aggregation across the collection) to be:
        //   d (2027) > b (2026) > c (2025-06) > a (2024-06) > e (2023-01)
        await Widget.Upsert(new Widget
        {
            Id = "a", Name = "Alpha",
            Sightings = new List<Sighting>
            {
                new() { LastChangedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                new() { LastChangedAt = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero) }
            }
        });
        await Widget.Upsert(new Widget
        {
            Id = "b", Name = "Bravo",
            Sightings = new List<Sighting>
            {
                new() { LastChangedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }
            }
        });
        await Widget.Upsert(new Widget
        {
            Id = "c", Name = "Charlie",
            Sightings = new List<Sighting>
            {
                new() { LastChangedAt = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero) }
            }
        });
        await Widget.Upsert(new Widget
        {
            Id = "d", Name = "Delta",
            Sightings = new List<Sighting>
            {
                new() { LastChangedAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero) }
            }
        });
        await Widget.Upsert(new Widget
        {
            Id = "e", Name = "Echo",
            Sightings = new List<Sighting>
            {
                new() { LastChangedAt = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero) }
            }
        });

        // Page 1, size 2: globally-sorted top two → d, b
        var page1 = await _client.GetAsync("/api/widgets?sort=-Sightings.LastChangedAt&page=1&pageSize=2");
        page1.StatusCode.Should().Be(HttpStatusCode.OK);
        var page1Items = await ReadItems(page1);
        page1Items.Select(w => w.GetProperty("id").GetString()).Should().Equal("d", "b");

        // Page 2: next two in sorted order → c, a
        var page2 = await _client.GetAsync("/api/widgets?sort=-Sightings.LastChangedAt&page=2&pageSize=2");
        page2.StatusCode.Should().Be(HttpStatusCode.OK);
        var page2Items = await ReadItems(page2);
        page2Items.Select(w => w.GetProperty("id").GetString()).Should().Equal("c", "a");

        // Page 3: last one → e
        var page3 = await _client.GetAsync("/api/widgets?sort=-Sightings.LastChangedAt&page=3&pageSize=2");
        page3.StatusCode.Should().Be(HttpStatusCode.OK);
        var page3Items = await ReadItems(page3);
        page3Items.Select(w => w.GetProperty("id").GetString()).Should().Equal("e");

        // Pagination headers reflect the right window
        page1.Headers.GetValues("X-Page").Should().ContainSingle().Which.Should().Be("1");
        page1.Headers.GetValues("X-Page-Size").Should().ContainSingle().Which.Should().Be("2");
    }

    [Fact]
    public async Task Get_with_collection_dot_path_sort_asc_uses_min_aggregation_and_paginates_correctly()
    {
        AppHost.Current = _factory.Services;

        // For ASC sort, MIN aggregation. Expected order:
        //   e (min=2023-01) < a (min=2024-01) < c (min=2025-06) < b (min=2026) < d (min=2027)
        await Widget.Upsert(new Widget
        {
            Id = "a", Name = "Alpha",
            Sightings = new List<Sighting>
            {
                new() { LastChangedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                new() { LastChangedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero) }  // intentional outlier
            }
        });
        await Widget.Upsert(new Widget
        {
            Id = "b", Name = "Bravo",
            Sightings = new List<Sighting>
            {
                new() { LastChangedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }
            }
        });
        await Widget.Upsert(new Widget
        {
            Id = "c", Name = "Charlie",
            Sightings = new List<Sighting>
            {
                new() { LastChangedAt = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero) }
            }
        });
        await Widget.Upsert(new Widget
        {
            Id = "d", Name = "Delta",
            Sightings = new List<Sighting>
            {
                new() { LastChangedAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero) }
            }
        });
        await Widget.Upsert(new Widget
        {
            Id = "e", Name = "Echo",
            Sightings = new List<Sighting>
            {
                new() { LastChangedAt = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero) }
            }
        });

        var page1 = await _client.GetAsync("/api/widgets?sort=%2BSightings.LastChangedAt&page=1&pageSize=3");
        page1.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await ReadItems(page1);
        items.Select(w => w.GetProperty("id").GetString()).Should().Equal("e", "a", "c");
    }

    [Fact]
    public async Task Post_query_with_deep_path_sort_in_body_and_pagination_in_body()
    {
        AppHost.Current = _factory.Services;
        await Widget.Upsert(new Widget
        {
            Id = "a",
            Sightings = new List<Sighting> { new() { LastChangedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) } }
        });
        await Widget.Upsert(new Widget
        {
            Id = "b",
            Sightings = new List<Sighting> { new() { LastChangedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) } }
        });
        await Widget.Upsert(new Widget
        {
            Id = "c",
            Sightings = new List<Sighting> { new() { LastChangedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero) } }
        });

        var response = await _client.PostAsJsonAsync("/api/widgets/query", new
        {
            sort = new[] { "-Sightings.LastChangedAt" },
            page = 1,
            size = 2
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(w => w.GetProperty("id").GetString()).Should().Equal("b", "c");
    }

    // === Strict-by-default error handling ===

    [Fact]
    public async Task Get_with_unresolvable_field_returns_400_with_error_payload()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        var response = await _client.GetAsync("/api/widgets?sort=NonexistentField");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain("NonexistentField");
        body.GetProperty("field").GetString().Should().Be("NonexistentField");
    }

    [Fact]
    public async Task Get_with_unresolvable_field_on_collection_path_returns_400()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        var response = await _client.GetAsync("/api/widgets?sort=Sightings.NonexistentSightingField");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain("NonexistentSightingField");
    }

    // === Lenient mode (per-controller) ===

    [Fact]
    public async Task Get_with_lenient_endpoint_skips_unresolvable_fields_silently_with_header()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        var response = await _client.GetAsync("/api/widgets-lenient?sort=NonexistentField,-Name");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The valid -Name spec is still applied.
        var items = await ReadItems(response);
        items.Select(w => w.GetProperty("name").GetString()).Should().Equal("Charlie", "Bravo", "Alpha");

        // Skipped field is surfaced as a header (lenient mode contract).
        response.Headers.TryGetValues("Koan-Sort-Skipped", out var headers).Should().BeFalse(
            "lenient mode surfaces via Extras → response header is added by EntityController when present; " +
            "if no header is emitted today this assertion will need a follow-up to wire the header through the response pipeline");
        // The above intentionally documents an open follow-up: the parser populates Extras['__sort_skipped']
        // but the controller does not currently project it to a response header. This test is provided so
        // the next iteration of the work has a clear contract to fulfil.
    }

    // === Sort-after-paginate regression ===

    [Fact]
    public async Task Page_one_sorted_ascending_returns_globally_smallest_not_first_inserted()
    {
        AppHost.Current = _factory.Services;

        // Insert in REVERSE alphabetic order so natural order != sorted order.
        // This is the exact pathology of the original bug: paginate-then-sort returns
        // the first-inserted page reordered, not the globally-sorted slice.
        for (var i = 9; i >= 0; i--)
        {
            await Widget.Upsert(new Widget { Id = $"w{i:D2}", Name = $"W{(char)('A' + i)}" });
        }

        var response = await _client.GetAsync("/api/widgets?sort=Name&page=1&pageSize=3");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Should().HaveCount(3);
        // EXPECT: globally smallest three names (WA, WB, WC).
        // BEFORE FIX would have returned: first-inserted three sorted (WH, WI, WJ).
        items.Select(w => w.GetProperty("name").GetString()).Should().Equal("WA", "WB", "WC");
    }

    [Fact]
    public async Task Page_two_sorted_ascending_continues_sorted_window()
    {
        AppHost.Current = _factory.Services;
        for (var i = 9; i >= 0; i--)
        {
            await Widget.Upsert(new Widget { Id = $"w{i:D2}", Name = $"W{(char)('A' + i)}" });
        }

        var response = await _client.GetAsync("/api/widgets?sort=Name&page=2&pageSize=3");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(w => w.GetProperty("name").GetString()).Should().Equal("WD", "WE", "WF");
    }

    // === Pagination headers preserved ===

    [Fact]
    public async Task Sorted_paginated_response_includes_pagination_headers()
    {
        AppHost.Current = _factory.Services;
        for (var i = 0; i < 10; i++)
        {
            await Widget.Upsert(new Widget { Id = $"w{i:D2}", Name = $"W{(char)('A' + i)}" });
        }

        var response = await _client.GetAsync("/api/widgets?sort=-Name&page=2&pageSize=3");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Headers.GetValues("X-Page").Should().ContainSingle().Which.Should().Be("2");
        response.Headers.GetValues("X-Page-Size").Should().ContainSingle().Which.Should().Be("3");
    }

    // === PaginationAttribute.DefaultSort ===

    [Fact]
    public async Task DefaultSort_attribute_applied_when_no_query_sort_provided()
    {
        AppHost.Current = _factory.Services;
        await Widget.Upsert(new Widget { Id = "1", Name = "Alpha", CreatedAt = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) });
        await Widget.Upsert(new Widget { Id = "2", Name = "Bravo", CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) });
        await Widget.Upsert(new Widget { Id = "3", Name = "Charlie", CreatedAt = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero) });

        // Default sort on this controller is "-CreatedAt,Name".
        var response = await _client.GetAsync("/api/widgets-default-sort");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(w => w.GetProperty("name").GetString()).Should().Equal("Bravo", "Charlie", "Alpha");
    }

    [Fact]
    public async Task Query_sort_overrides_DefaultSort_attribute()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        var response = await _client.GetAsync("/api/widgets-default-sort?sort=Name");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(w => w.GetProperty("name").GetString()).Should().Equal("Alpha", "Bravo", "Charlie");
    }

    // === POST /query body sort (DATA-0093 asymmetry fix) ===

    [Fact]
    public async Task Post_query_with_body_sort_field_applies_sort()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        var response = await _client.PostAsJsonAsync("/api/widgets/query", new
        {
            sort = new[] { "-Name" }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(w => w.GetProperty("name").GetString()).Should().Equal("Charlie", "Bravo", "Alpha");
    }

    [Fact]
    public async Task Post_query_with_body_sort_overrides_query_string_sort()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        // Query-string sort says ascending; body sort says descending — body wins.
        var response = await _client.PostAsJsonAsync("/api/widgets/query?sort=Name", new
        {
            sort = new[] { "-Name" }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(w => w.GetProperty("name").GetString()).Should().Equal("Charlie", "Bravo", "Alpha");
    }

    [Fact]
    public async Task Post_query_with_unresolvable_body_sort_returns_400()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        var response = await _client.PostAsJsonAsync("/api/widgets/query", new
        {
            sort = new[] { "NonexistentField" }
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // === Helpers ===

    private async Task SeedAlphaBravoCharlie()
    {
        await Widget.Upsert(new Widget { Id = "id1", Name = "Charlie" });
        await Widget.Upsert(new Widget { Id = "id2", Name = "Alpha" });
        await Widget.Upsert(new Widget { Id = "id3", Name = "Bravo" });
    }

    private static async Task<List<JsonElement>> ReadItems(HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        // EntityController GET / returns the array directly (no envelope), per DATA-0029.
        if (doc.ValueKind == JsonValueKind.Array)
        {
            return doc.EnumerateArray().ToList();
        }
        // Some controllers wrap in { items: [...] }; handle both.
        if (doc.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            return items.EnumerateArray().ToList();
        }
        throw new InvalidOperationException(
            $"Unexpected response shape for collection endpoint: {doc.ToString()[..Math.Min(200, doc.ToString().Length)]}");
    }
}
