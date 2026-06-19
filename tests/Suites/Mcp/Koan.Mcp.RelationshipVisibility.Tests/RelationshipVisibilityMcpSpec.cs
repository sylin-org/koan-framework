using System.Threading.Tasks;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.RelationshipVisibility.Tests;

/// <summary>
/// AN-leak (MCP path) — the governed relationship expansion landed in <c>EntityEndpointService</c> must
/// also hold over MCP, which routes <c>get-by-id</c> with <c>with: "all"</c> through the same service.
/// The anonymous MCP caller (no HttpContext, no principal) takes each hook's anonymous branch, so Draft
/// works and a Secret author must never surface through the MCP expansion.
/// </summary>
public sealed class RelationshipVisibilityMcpSpec : IClassFixture<RelationshipVisibilityFixture>, IAsyncLifetime
{
    private readonly RelationshipVisibilityFixture _fx;
    private IDisposable? _scope;

    public RelationshipVisibilityMcpSpec(RelationshipVisibilityFixture fx) => _fx = fx;

    public async ValueTask InitializeAsync()
    {
        _scope = AppHost.PushScope(_fx.Services);
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

    [Fact]
    public async Task Mcp_get_by_id_with_all_omits_walled_child_rows()
    {
        var tool = _fx.ResolveToolName("maker", EntityEndpointOperationKind.GetById);
        var result = await _fx.CallToolAsync(tool, new JObject { ["id"] = "m1", ["with"] = "all" });

        McpHarnessFixtureBase.IsError(result).Should().BeFalse();
        var text = McpHarnessFixtureBase.ContentText(result);
        text.Should().NotBeNullOrEmpty();

        text!.Should().Contain("authored-pub-1",
            "published authored works are visible to the anonymous MCP caller");
        text.Should().NotContain("reviewed-draft-1").And.NotContain("reviewed-draft-2",
            "Draft works the Work predicate walls must never tunnel out through the MCP expansion path");
    }

    [Fact]
    public async Task Mcp_get_by_id_with_all_omits_walled_parent()
    {
        var tool = _fx.ResolveToolName("work", EntityEndpointOperationKind.GetById);
        var result = await _fx.CallToolAsync(tool, new JObject { ["id"] = "ws", ["with"] = "all" });

        McpHarnessFixtureBase.IsError(result).Should().BeFalse();
        var text = McpHarnessFixtureBase.ContentText(result);
        text.Should().NotBeNullOrEmpty();

        text!.Should().Contain("work-secret-author", "the work itself is visible");
        text.Should().NotContain("secret-maker", "the walled author parent must be omitted over MCP too");
    }

    private static async Task Seed()
    {
        await Maker.Upsert(new Maker { Id = "m1", Name = "maker-one", Secret = false });
        await Maker.Upsert(new Maker { Id = "ms", Name = "secret-maker", Secret = true });

        await Work.Upsert(new Work { Id = "w1", Title = "authored-pub-1", AuthorId = "m1", Status = WorkStatus.Published });
        await Work.Upsert(new Work { Id = "w3", Title = "reviewed-draft-1", ReviewerId = "m1", Status = WorkStatus.Draft });
        await Work.Upsert(new Work { Id = "w4", Title = "reviewed-draft-2", ReviewerId = "m1", Status = WorkStatus.Draft });
        await Work.Upsert(new Work { Id = "ws", Title = "work-secret-author", AuthorId = "ms", Status = WorkStatus.Published });
    }
}
