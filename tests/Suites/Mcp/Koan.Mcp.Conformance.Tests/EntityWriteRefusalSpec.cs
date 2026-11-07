using System.Threading.Tasks;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Mcp.Options;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Koan.Web.Hooks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>WEB-0086: a note whose hook refuses what its title says.</summary>
[McpEntity(Name = "pledge", Exposure = McpExposureMode.Full)]
[StorageName("conformance_pledges")]
public sealed class Pledge : Entity<Pledge>
{
    public string Title { get; set; } = "";
}

/// <summary>Refuses a pledge titled "nul" on its field, and one titled "late" as a conflict of its own word.</summary>
public sealed class PledgeHook : IModelHook<Pledge>
{
    public int Order => 0;
    public Task OnBeforeFetch(HookContext<Pledge> ctx, string id) => Task.CompletedTask;
    public Task OnAfterFetch(HookContext<Pledge> ctx, Pledge? model) => Task.CompletedTask;
    public Task OnBeforePatch(HookContext<Pledge> ctx, string id, object patch) => Task.CompletedTask;
    public Task OnAfterPatch(HookContext<Pledge> ctx, Pledge model) => Task.CompletedTask;
    public Task OnBeforeDelete(HookContext<Pledge> ctx, Pledge model) => Task.CompletedTask;
    public Task OnAfterDelete(HookContext<Pledge> ctx, Pledge model) => Task.CompletedTask;

    public Task OnBeforeSave(HookContext<Pledge> ctx, Pledge model) => model.Title == "nul"
        ? throw new Refusal(Reason.Invalid("title", "A title cannot be that."))
        : Task.CompletedTask;

    public Task OnAfterSave(HookContext<Pledge> ctx, Pledge model) => model.Title == "late"
        ? throw new Refusal(Reason.Conflict("too-late", "This pledge came too late."))
        : Task.CompletedTask;
}

public sealed class PledgeFixture : McpHarnessFixtureBase
{
    protected override void ConfigureMcp(McpServerOptions options) => options.AllowedEntities.Add("pledge");

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddScoped<IModelHook<Pledge>, PledgeHook>();
}

/// <summary>WEB-0086: a refusal a hook throws answers an entity tool as the same problem REST answers, kind, word and field.</summary>
public sealed class EntityWriteRefusalSpec(PledgeFixture fx) : IClassFixture<PledgeFixture>
{
    private string Upsert => fx.ResolveToolName("pledge", EntityEndpointOperationKind.Upsert);

    [Fact]
    public async Task A_refusal_on_a_field_is_a_failed_call_carrying_its_problem()
    {
        var call = await fx.CallToolAsync(Upsert, new JObject { ["model"] = new JObject { ["title"] = "nul" } });

        McpHarnessFixtureBase.IsError(call).Should().BeTrue(call.ToString());
        call["_meta"]!["status"]!.Value<int>().Should().Be(400);
        var problem = call["_meta"]!["problem"]!;
        problem["kind"]!.Value<string>().Should().Be("invalid");
        problem["word"]!.Value<string>().Should().Be("invalid");
        problem["errors"]!["title"]![0]!.Value<string>().Should().Be("A title cannot be that.");
        McpHarnessFixtureBase.ContentText(call).Should().Contain("title: A title cannot be that.");
    }

    [Fact]
    public async Task A_refusal_with_its_own_word_keeps_its_status_and_word()
    {
        var call = await fx.CallToolAsync(Upsert, new JObject { ["model"] = new JObject { ["title"] = "late" } });

        McpHarnessFixtureBase.IsError(call).Should().BeTrue(call.ToString());
        call["_meta"]!["status"]!.Value<int>().Should().Be(409);
        call["_meta"]!["problem"]!["word"]!.Value<string>().Should().Be("too-late");
        call["_meta"]!["problem"]!["detail"]!.Value<string>().Should().Be("This pledge came too late.");
    }
}
