using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Core.Hosting.App;
using Koan.Data.Core;

namespace Koan.Web.Sort.Tests.Specs;

/// <summary>
/// Guards the X-entitycontroller-query-parse fix (ARCH / EntityController POST /query): the handler
/// previously wrapped body parsing in a bare <c>catch {}</c>, so a malformed body or non-numeric
/// page/size silently fell through to DEFAULTS and returned 200 — most dangerously, a non-object body
/// dropped the filter and returned UNFILTERED results. The chosen contract is GET-parity: a non-object
/// body and a negative page return 400; non-coercible page/size are ignored (treated as absent) exactly
/// like the GET-list path; coercion never throws.
/// </summary>
public sealed class QueryParseSpecs : IClassFixture<SortWebApplicationFactory>, IAsyncLifetime
{
    private readonly SortWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public QueryParseSpecs(SortWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async ValueTask InitializeAsync()
    {
        AppHost.Current = _factory.Services;
        await Widget.RemoveAll();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // === The dangerous case: a non-object body must NOT silently drop the filter and return everything ===

    [Fact]
    public async Task Post_query_with_non_object_body_returns_400_not_unfiltered_results()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        // A JSON array body binds to the [FromBody] object but is not a query object. Before the fix the
        // bare catch swallowed JObject.Parse and ran the query with DEFAULTS — returning all three widgets.
        var response = await _client.PostAsJsonAsync("/api/widgets/query", new[] { 1, 2, 3 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // === Negative page is rejected (GET-parity: "page must be >= 0") ===

    [Fact]
    public async Task Post_query_with_negative_page_returns_400()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        var response = await _client.PostAsJsonAsync("/api/widgets/query", new { page = -1 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain("page");
    }

    // === Non-coercible page/size are ignored, not 500 and not 400 (GET-parity leniency) ===

    [Fact]
    public async Task Post_query_with_non_numeric_page_is_ignored_and_returns_200()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        // Before the fix the (int)page cast threw FormatException → swallowed → default page (still 200,
        // but via a swallowed exception). After the fix this is coerced safely and ignored — GET-parity.
        var response = await _client.PostAsJsonAsync("/api/widgets/query", new { page = "abc", size = "xyz" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await ReadItems(response);
        items.Should().HaveCount(3);
    }

    // === An empty query object is valid: no filter, default paging, all results ===

    [Fact]
    public async Task Post_query_with_empty_object_body_returns_all_results()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        var response = await _client.PostAsJsonAsync("/api/widgets/query", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await ReadItems(response);
        items.Should().HaveCount(3);
    }

    // === Valid page/size in the body still applies (the happy path is unbroken) ===

    [Fact]
    public async Task Post_query_with_valid_paging_in_body_applies_the_window()
    {
        AppHost.Current = _factory.Services;
        await SeedAlphaBravoCharlie();

        var response = await _client.PostAsJsonAsync("/api/widgets/query", new { sort = new[] { "Name" }, page = 1, size = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await ReadItems(response);
        items.Should().HaveCount(2);
        items.Select(w => w.GetProperty("name").GetString()).Should().Equal("Alpha", "Bravo");
    }

    private async Task SeedAlphaBravoCharlie()
    {
        await Widget.Upsert(new Widget { Id = "id1", Name = "Charlie" });
        await Widget.Upsert(new Widget { Id = "id2", Name = "Alpha" });
        await Widget.Upsert(new Widget { Id = "id3", Name = "Bravo" });
    }

    private static async Task<List<JsonElement>> ReadItems(HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (doc.ValueKind == JsonValueKind.Array)
        {
            return doc.EnumerateArray().ToList();
        }
        if (doc.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            return items.EnumerateArray().ToList();
        }
        throw new InvalidOperationException(
            $"Unexpected response shape for collection endpoint: {doc.ToString()[..Math.Min(200, doc.ToString().Length)]}");
    }
}
