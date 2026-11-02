using System.Linq;
using System.Threading.Tasks;
using Koan.Mcp.Options;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Koan.Web.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>Boots <see cref="Leaf"/> under configured bounds: at most 5 per page, at most 8 unpaged.</summary>
public sealed class PagingFixture : McpHarnessFixtureBase
{
    public const int Rows = 12;

    protected override void ConfigureMcp(McpServerOptions options) => options.AllowedEntities.Add("leaf");

    protected override void ConfigureServices(IServiceCollection services)
        => services.Configure<PaginationSafetyBounds>(bounds =>
        {
            bounds.MaxPageSize = 5;
            bounds.AbsoluteMaxRecords = 8;
        });

    private bool _seeded;

    public async Task EnsureSeeded()
    {
        if (_seeded) return;
        _seeded = true;
        var models = new JArray(Enumerable.Range(1, Rows).Select(i => new JObject { ["name"] = $"n{i:00}" }));
        var seeded = await CallToolAsync(ResolveToolName("leaf", EntityEndpointOperationKind.UpsertMany), new JObject { ["models"] = models });
        if (IsError(seeded)) throw new System.InvalidOperationException(seeded.ToString());
    }
}

/// <summary>
/// WEB-0076: the MCP collection honors page and pageSize, every paged MCP read is clamped to the configured bounds, and
/// an unpaged read over the safety limit is refused. Code-mode paging walks disjoint pages.
/// </summary>
public sealed class McpPagingSpec : IClassFixture<PagingFixture>
{
    private readonly PagingFixture _fx;

    public McpPagingSpec(PagingFixture fx) => _fx = fx;

    private string Tool(EntityEndpointOperationKind operation) => _fx.ResolveToolName("leaf", operation);

    private async Task<JToken> Call(string tool, JObject args)
    {
        await _fx.EnsureSeeded();
        return await _fx.CallToolAsync(tool, args);
    }

    private static string[] Names(JToken call)
        => JArray.Parse(McpHarnessFixtureBase.ContentText(call)!).Select(row => row["name"]!.Value<string>()!).ToArray();

    [Fact]
    public async Task The_collection_serves_the_requested_page()
    {
        var call = await Call(Tool(EntityEndpointOperationKind.Collection),
            new JObject { ["page"] = 2, ["pageSize"] = 3, ["sort"] = "name" });

        McpHarnessFixtureBase.IsError(call).Should().BeFalse(call.ToString());
        Names(call).Should().Equal("n04", "n05", "n06");
        call["_meta"]!["headers"]!["X-Page"]!.Value<string>().Should().Be("2");
    }

    [Fact]
    public async Task A_page_size_above_the_configured_maximum_is_clamped()
    {
        var collection = await Call(Tool(EntityEndpointOperationKind.Collection), new JObject { ["page"] = 1, ["pageSize"] = 100 });
        var query = await Call(Tool(EntityEndpointOperationKind.Query), new JObject { ["pageSize"] = 100000000 });

        Names(collection).Should().HaveCount(5, collection.ToString());
        Names(query).Should().HaveCount(5, query.ToString());
    }

    [Fact]
    public async Task An_unpaged_collection_over_the_safety_limit_is_refused()
    {
        var call = await Call(Tool(EntityEndpointOperationKind.Collection), new JObject());

        McpHarnessFixtureBase.IsError(call).Should().BeTrue(call.ToString());
        call["_meta"]!["diagnostics"]!["shortCircuitStatusCode"]!.Value<int>().Should().Be(413);
    }

    [Fact]
    public async Task Code_mode_pages_are_disjoint()
    {
        const string code = "const a = SDK.Entities.leaf.page(1, 3, { sort: 'name' }); const b = SDK.Entities.leaf.page(2, 3, { sort: 'name' });" +
                            "SDK.Out.answer(JSON.stringify({ a: a.items.map(x => x.name), b: b.items.map(x => x.name) }));";
        var call = await Call("koan_code_execute", new JObject { ["code"] = code });

        var answer = JObject.Parse(McpHarnessFixtureBase.ContentText(call)!);
        answer["a"]!.Values<string>().Should().Equal("n01", "n02", "n03");
        answer["b"]!.Values<string>().Should().Equal("n04", "n05", "n06");
    }
}
