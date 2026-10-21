using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp.Options;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>WEB-0077: a row whose model hook refuses deletion while locked.</summary>
[McpEntity(Name = "crate", Exposure = McpExposureMode.Full)]
[StorageName("conformance_crates")]
public sealed class Crate : Entity<Crate>
{
    public string Tag { get; set; } = "";
    public bool Locked { get; set; }
}

/// <summary>Refuses deleting a locked crate and records the deletes it sees, by tag.</summary>
public sealed class CrateDeleteHook : IModelHook<Crate>
{
    public static readonly ConcurrentQueue<string> Before = new();
    public static readonly ConcurrentQueue<string> After = new();

    public int Order => 0;
    public Task OnBeforeFetch(HookContext<Crate> ctx, string id) => Task.CompletedTask;
    public Task OnAfterFetch(HookContext<Crate> ctx, Crate? model) => Task.CompletedTask;
    public Task OnBeforeSave(HookContext<Crate> ctx, Crate model) => Task.CompletedTask;
    public Task OnAfterSave(HookContext<Crate> ctx, Crate model) => Task.CompletedTask;
    public Task OnBeforePatch(HookContext<Crate> ctx, string id, object patch) => Task.CompletedTask;
    public Task OnAfterPatch(HookContext<Crate> ctx, Crate model) => Task.CompletedTask;

    public Task OnBeforeDelete(HookContext<Crate> ctx, Crate model)
    {
        Before.Enqueue(model.Tag);
        if (model.Locked) ctx.ShortCircuit(new ConflictObjectResult(new { error = "locked" }));
        return Task.CompletedTask;
    }

    public Task OnAfterDelete(HookContext<Crate> ctx, Crate model)
    {
        After.Enqueue(model.Tag);
        return Task.CompletedTask;
    }
}

public sealed class CrateFixture : McpHarnessFixtureBase
{
    protected override void ConfigureMcp(McpServerOptions options) => options.AllowedEntities.Add("crate");

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddScoped<IModelHook<Crate>, CrateDeleteHook>();
}

/// <summary>WEB-0077: the MCP bulk delete tools raise the same model delete hooks as a single delete.</summary>
public sealed class BulkDeleteHookParitySpec : IClassFixture<CrateFixture>
{
    private readonly CrateFixture _fx;

    public BulkDeleteHookParitySpec(CrateFixture fx) => _fx = fx;

    private string Tool(EntityEndpointOperationKind operation) => _fx.ResolveToolName("crate", operation);

    [Fact]
    public async Task A_locked_row_survives_every_bulk_delete_tool()
    {
        var locked = await Create("mcp-locked", locked: true);

        var many = await _fx.CallToolAsync(Tool(EntityEndpointOperationKind.DeleteMany), new JObject { ["ids"] = new JArray(locked) });
        var byQuery = await _fx.CallToolAsync(Tool(EntityEndpointOperationKind.DeleteByQuery), new JObject { ["query"] = "{\"tag\":\"mcp-locked\"}" });
        var all = await _fx.CallToolAsync(Tool(EntityEndpointOperationKind.DeleteAll), new JObject());

        foreach (var call in new[] { many, byQuery, all })
        {
            McpHarnessFixtureBase.IsError(call).Should().BeTrue(call.ToString());
            call["meta"]!["diagnostics"]!["shortCircuitStatusCode"]!.Value<int>().Should().Be(409);
        }
        var still = await _fx.CallToolAsync(Tool(EntityEndpointOperationKind.GetById), new JObject { ["id"] = locked });
        McpHarnessFixtureBase.IsError(still).Should().BeFalse("the locked crate survives: {0}", still);
    }

    [Fact]
    public async Task A_dry_run_raises_before_delete_but_not_after_delete()
    {
        var row = await Create("mcp-dry-run");

        var call = await _fx.CallToolAsync(Tool(EntityEndpointOperationKind.DeleteMany),
            new JObject { ["ids"] = new JArray(row), ["dry_run"] = true });

        McpHarnessFixtureBase.IsError(call).Should().BeFalse(call.ToString());
        CrateDeleteHook.Before.Should().Contain("mcp-dry-run");
        CrateDeleteHook.After.Should().NotContain("mcp-dry-run");
    }

    private async Task<string> Create(string tag, bool locked = false)
    {
        var call = await _fx.CallToolAsync(Tool(EntityEndpointOperationKind.Upsert),
            new JObject { ["model"] = new JObject { ["tag"] = tag, ["locked"] = locked } });
        McpHarnessFixtureBase.IsError(call).Should().BeFalse(call.ToString());
        return JObject.Parse(McpHarnessFixtureBase.ContentText(call)!)["id"]!.Value<string>()!;
    }
}
