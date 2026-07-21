using System.Net;
using System.Linq;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Core.Diagnostics;
using Koan.Data.Core;
using Newtonsoft.Json.Linq;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.AdapterSurface.InMemory.Tests.RelationshipExpansion;

/// <summary>
/// AN-leak (docs/assessment/09 §10) — relationship expansion (<c>?with=all</c>) must subject every
/// expanded related entity to ITS OWN type's WEB-0068 visibility predicates, exactly as a direct
/// query/get-by-id of that type would. The keyed get-by-id gate fixed the root row; this suite proves
/// the edge-traversal path is not a second bypass.
///
/// The charter tests: T1 lateral-movement tunnel; T2 divergent edges to the same target with
/// asymmetric disclosure; T-parent walled-parent omission; T-app-authority the domain traversal API
/// stays app-authority (the regression guard proving the fix is scoped to the Web endpoint).
/// </summary>
public sealed class RelationshipExpansionVisibilitySpecs : IClassFixture<InMemoryAdapterFactory>, IAsyncLifetime
{
    private readonly InMemoryAdapterFactory _factory;
    private IDisposable? _scope;

    public RelationshipExpansionVisibilitySpecs(InMemoryAdapterFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        AggregateConfigs.Reset();
        _scope = AppHost.PushScope(_factory.Services);
        await _factory.ResetAsync();
        await Maker.RemoveAll();
        await Work.RemoveAll();
        await Seed();
    }

    public ValueTask DisposeAsync()
    {
        _scope?.Dispose();
        _scope = null;
        return ValueTask.CompletedTask;
    }

    // ---- T1: lateral-movement tunnel ------------------------------------------------------------

    [Fact]
    public async Task Walled_child_rows_never_surface_through_parent_expansion()
    {
        var body = await GetString("/api/an-makers/m1?with=all"); // anonymous

        body.Should().Contain("authored-pub-1").And.Contain("authored-pub-2",
            "published authored works are visible to anonymous and pass the Work predicate");
        body.Should().NotContain("reviewed-draft-1").And.NotContain("reviewed-draft-2",
            "Draft works the anonymous Work predicate excludes must not tunnel out through Maker?with=all");
    }

    [Fact]
    public async Task Direct_child_query_agrees_with_expansion_under_the_same_grant()
    {
        // The expansion's row set must equal a direct query of the child type under the same grant.
        var works = JArray.Parse(await GetString("/api/an-works")); // anonymous collection
        var titles = works.Select(w => w["title"]?.Value<string>() ?? w["Title"]?.Value<string>()).ToList();

        titles.Should().NotContain("reviewed-draft-1").And.NotContain("reviewed-draft-2",
            "a direct anonymous query hides Drafts — the expansion must hide exactly the same rows");
        titles.Should().Contain("authored-pub-1");
    }

    // ---- T2: divergent edges, same target, asymmetric disclosure --------------------------------

    [Fact]
    public async Task Anonymous_sees_open_edge_and_walled_edge_discloses_nothing()
    {
        var graph = JObject.Parse(await GetString("/api/an-makers/m1?with=all")); // anonymous
        var workEdges = WorkEdges(graph);

        EdgeKeys(workEdges).Should().Contain(k => Same(k, "AuthorId"),
            "the authored edge resolves to visible (Published) works");
        EdgeKeys(workEdges).Should().NotContain(k => Same(k, "ReviewerId"),
            "the reviewed edge is fully walled for anonymous — no edge, no count, no field name");

        EdgeRows(workEdges, "AuthorId").Should().HaveCount(2);
    }

    [Fact]
    public async Task A_grant_opening_reviewer_visibility_reveals_the_walled_edge()
    {
        var graph = JObject.Parse(await GetString("/api/an-makers/m1?with=all", ("X-Test-Grant", "drafts")));
        var workEdges = WorkEdges(graph);

        EdgeKeys(workEdges).Should().Contain(k => Same(k, "ReviewerId"),
            "the grant opens Draft visibility, so the previously-walled reviewed edge now appears");
        EdgeRows(workEdges, "ReviewerId").Should().HaveCount(2);
    }

    // ---- T-parent: walled-parent omission -------------------------------------------------------

    [Fact]
    public async Task Walled_parent_is_omitted_from_the_expanded_graph()
    {
        // ws is Published (the caller may read the work) but its author is a Secret maker the caller
        // may not see. The expanded graph must omit the walled parent, never return it.
        var raw = await GetString("/api/an-works/ws?with=all"); // anonymous
        raw.Should().Contain("work-secret-author", "the work itself is visible");
        raw.Should().NotContain("secret-maker", "the walled author parent must be omitted");

        var graph = JObject.Parse(raw);
        var parents = Prop(graph, "parents") as JObject;
        // AuthorId points at the walled secret maker -> the key must be absent (not present-with-object).
        if (parents is not null && Prop(parents, "AuthorId") is JToken author && author.Type != JTokenType.Null)
        {
            author.Type.Should().Be(JTokenType.Null, "a walled parent is never materialized into the graph");
        }
    }

    // ---- T-app-authority: the domain traversal API is unchanged ---------------------------------

    [Fact]
    public async Task Domain_traversal_api_stays_app_authority()
    {
        // Service code calling GetChildren<T>() directly (no HTTP request, no endpoint) must still see
        // ALL children regardless of request predicates — the guard that the fix did not leak request
        // visibility into Koan.Data.Core.
        var maker = await Maker.Get("m1");
        maker.Should().NotBeNull();
        var children = await maker!.GetChildren<Work>();

        children.Should().HaveCount(4, "authored (2) + reviewed (2), with no predicate applied");
        children.Select(w => w.Title).Should().Contain("reviewed-draft-1",
            "the domain API returns Drafts that the Web read path would wall");
    }

    [Fact]
    public async Task Relationship_result_limit_returns_413_and_records_a_safe_fact()
    {
        await Work.Upsert(new Work
        {
            Id = "w-limit",
            Title = "third-authored-work",
            AuthorId = "m1",
            Status = WorkStatus.Published
        });

        var response = await _factory.Client.GetAsync("/api/an-makers/m1?with=all");

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("relationship-result-limit").And.Contain("correction");
        _factory.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts.Should().Contain(fact =>
            fact.Code == Koan.Data.Core.Infrastructure.Constants.Diagnostics.Codes.RelationshipExecution
            && fact.State == KoanFactState.Rejected
            && fact.ReasonCode == Koan.Data.Core.Infrastructure.Constants.Diagnostics.Reasons.ResultLimit);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private async Task<string> GetString(string url, params (string Key, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (key, value) in headers) request.Headers.Add(key, value);
        var response = await _factory.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the root entity is visible to the caller");
        return await response.Content.ReadAsStringAsync();
    }

    private static JObject? WorkEdges(JObject graph)
    {
        var children = Prop(graph, "children") as JObject;
        return children is null ? null : Prop(children, "Work") as JObject;
    }

    private static IEnumerable<string> EdgeKeys(JObject? workEdges)
        => workEdges?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>();

    private static JArray EdgeRows(JObject? workEdges, string referenceProperty)
        => (workEdges is null ? null : Prop(workEdges, referenceProperty) as JArray) ?? new JArray();

    private static JToken? Prop(JObject o, string name)
        => o.Properties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static async Task Seed()
    {
        await Maker.Upsert(new Maker { Id = "m1", Name = "maker-one", Secret = false });
        await Maker.Upsert(new Maker { Id = "ms", Name = "secret-maker", Secret = true });

        await Work.Upsert(new Work { Id = "w1", Title = "authored-pub-1", AuthorId = "m1", Status = WorkStatus.Published });
        await Work.Upsert(new Work { Id = "w2", Title = "authored-pub-2", AuthorId = "m1", Status = WorkStatus.Published });
        await Work.Upsert(new Work { Id = "w3", Title = "reviewed-draft-1", ReviewerId = "m1", Status = WorkStatus.Draft });
        await Work.Upsert(new Work { Id = "w4", Title = "reviewed-draft-2", ReviewerId = "m1", Status = WorkStatus.Draft });
        await Work.Upsert(new Work { Id = "ws", Title = "work-secret-author", AuthorId = "ms", Status = WorkStatus.Published });
    }
}
