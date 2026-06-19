using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Xunit;

namespace Koan.Web.AdapterSurface.InMemory.Tests.PredicateHook;

/// <summary>
/// WEB-0068 — keyed get-by-id must honor the same <c>IRequestOptionsHook&lt;VisibilityWidget&gt;</c>
/// predicates the collection path AND-composes. Without the gate, a row hidden from every listing is
/// still reachable by id (a row-level visibility bypass): the original report was an MCP read surface
/// where <c>get-by-id</c> returned Suppressed/Draft rows the collection tool filtered out.
///
/// The hook (see <see cref="VisibilityHook"/>) excludes <c>Hidden</c> for everyone, narrows anonymous
/// callers to <c>Published</c>, and lets an authenticated owner also see their own <c>Draft</c>.
/// </summary>
public sealed class GetByIdVisibilitySpecs : IClassFixture<InMemoryAdapterFactory>, IAsyncLifetime
{
    private readonly InMemoryAdapterFactory _factory;
    private IDisposable? _scope;

    public GetByIdVisibilitySpecs(InMemoryAdapterFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        AggregateConfigs.Reset();
        _scope = AppHost.PushScope(_factory.Services);
        await _factory.ResetAsync();
        await VisibilityWidget.RemoveAll();
        await SeedMatrix();
    }

    public ValueTask DisposeAsync()
    {
        _scope?.Dispose();
        _scope = null;
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Anonymous_can_get_a_published_row()
    {
        var response = await GetById("1"); // pub-alice, Published
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await NameOf(response)).Should().Be("pub-alice");
    }

    [Fact]
    public async Task Anonymous_get_of_a_draft_is_NotFound()
    {
        // The row exists, but the anonymous predicate (Status == Published) excludes it. The keyed
        // read must return the same 404 as a missing row — never the Draft body.
        var response = await GetById("3"); // draft-alice, Draft
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Hidden_row_is_NotFound_for_everyone_by_id()
    {
        foreach (var headers in new[]
        {
            Array.Empty<(string, string)>(),
            new[] { ("X-Test-User", "alice") }, // owner of the hidden row
            new[] { ("X-Test-Role", "admin") },
        })
        {
            var response = await GetById("5", headers); // hidden-row, Hidden, owner alice
            response.StatusCode.Should().Be(HttpStatusCode.NotFound,
                "Hidden must never surface by id, even to its owner or an admin");
        }
    }

    [Fact]
    public async Task Owner_can_get_their_own_draft_by_id()
    {
        var response = await GetById("3", ("X-Test-User", "alice")); // draft-alice, owned by alice
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await NameOf(response)).Should().Be("draft-alice");
    }

    [Fact]
    public async Task Owner_cannot_get_someone_elses_draft_by_id()
    {
        var response = await GetById("4", ("X-Test-User", "alice")); // draft-bob, owned by bob
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_can_get_any_non_hidden_row_by_id()
    {
        var response = await GetById("4", ("X-Test-Role", "admin")); // draft-bob
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await NameOf(response)).Should().Be("draft-bob");
    }

    [Fact]
    public async Task Relationship_expansion_path_is_also_gated()
    {
        // ?with=all takes the GetRelatives branch in EntityEndpointService.GetById — the predicate
        // gate must run before it so the expansion path is not a second bypass.
        var response = await GetById("5?with=all"); // hidden-row, anonymous
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private Task<HttpResponseMessage> GetById(string idAndQuery, params (string Key, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/visibility-widgets/{idAndQuery}");
        foreach (var (key, value) in headers) request.Headers.Add(key, value);
        return _factory.Client.SendAsync(request);
    }

    private static async Task<string?> NameOf(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(raw).RootElement;
        return doc.TryGetProperty("name", out var n) ? n.GetString() : null;
    }

    private static async Task SeedMatrix()
    {
        await VisibilityWidget.Upsert(new VisibilityWidget { Id = "1", Name = "pub-alice", Status = VisibilityStatus.Published, OwnerId = "alice" });
        await VisibilityWidget.Upsert(new VisibilityWidget { Id = "2", Name = "pub-bob", Status = VisibilityStatus.Published, OwnerId = "bob" });
        await VisibilityWidget.Upsert(new VisibilityWidget { Id = "3", Name = "draft-alice", Status = VisibilityStatus.Draft, OwnerId = "alice" });
        await VisibilityWidget.Upsert(new VisibilityWidget { Id = "4", Name = "draft-bob", Status = VisibilityStatus.Draft, OwnerId = "bob" });
        await VisibilityWidget.Upsert(new VisibilityWidget { Id = "5", Name = "hidden-row", Status = VisibilityStatus.Hidden, OwnerId = "alice" });
    }
}
