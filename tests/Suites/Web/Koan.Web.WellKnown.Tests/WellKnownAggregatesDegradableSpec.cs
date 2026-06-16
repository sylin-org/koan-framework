using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// (X-f2-failure-coverage) Failure-path coverage for the F2-web burn-down (91651138) in
/// <c>WellKnownController.Aggregates</c> (<c>/.well-known/Koan/aggregates</c>): a per-aggregate capability
/// self-report fault must DEGRADE that one item (empty query/write tokens) and NEVER fail the whole endpoint.
/// <see cref="FaultyAggregate"/> is pinned to a non-existent adapter, so its repository resolution throws —
/// exactly the fault the broad degradable catch absorbs. Runs through a real <c>WebApplicationFactory</c> host.
/// </summary>
public sealed class WellKnownAggregatesDegradableSpec : IClassFixture<WellKnownWebApplicationFactory>
{
    private readonly HttpClient _client;

    public WellKnownAggregatesDegradableSpec(WellKnownWebApplicationFactory factory)
    {
        AppHost.Current = factory.Services;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Aggregates_degrades_a_faulty_aggregate_without_failing_the_endpoint()
    {
        var resp = await _client.GetAsync("/.well-known/Koan/aggregates");

        // The endpoint must NOT 500 despite a faulty aggregate's self-report throwing.
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var aggregates = doc.GetProperty("aggregates").EnumerateArray().ToList();
        static string TypeOf(JsonElement a) => a.GetProperty("type").GetString() ?? "";

        // The faulty aggregate is PRESENT but DEGRADED: empty tokens — not an omission, not a 500.
        var faulty = aggregates.Single(a => TypeOf(a).EndsWith(".FaultyAggregate"));
        faulty.GetProperty("query").GetArrayLength().Should().Be(0);
        faulty.GetProperty("write").GetArrayLength().Should().Be(0);

        // The healthy aggregate is still listed — the fault degraded only its own item, not the endpoint.
        aggregates.Any(a => TypeOf(a).EndsWith(".HealthyAggregate")).Should().BeTrue();
    }
}
