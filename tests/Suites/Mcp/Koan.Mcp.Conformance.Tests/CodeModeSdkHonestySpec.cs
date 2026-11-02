using System.Threading.Tasks;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp.Options;
using Koan.Mcp.TestKit;
using Koan.Web.Authorization;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>AI-0040: an entity whose remove needs a signed-in caller, so the anonymous STDIO sandbox is denied it.</summary>
[McpEntity(Name = "keepsake", Exposure = McpExposureMode.Full)]
[Access(remove: "authenticated")]
[StorageName("conformance_keepsakes")]
public sealed class Keepsake : Entity<Keepsake>
{
    public string Tag { get; set; } = "";
}

/// <summary>AI-0040: an open entity for the SDK count and paging cells.</summary>
[McpEntity(Name = "marble", Exposure = McpExposureMode.Full)]
[StorageName("conformance_marbles")]
public sealed class Marble : Entity<Marble>
{
    public string Tag { get; set; } = "";
}

public sealed class CodeModeSdkFixture : McpHarnessFixtureBase
{
    protected override void ConfigureMcp(McpServerOptions options)
    {
        options.AllowedEntities.Add("keepsake");
        options.AllowedEntities.Add("marble");
        options.AllowedEntities.Add("coffer");
    }
}

/// <summary>
/// AI-0040: the code-mode SDK reports what the endpoint did. A refusal throws; delete returns 1 or 0; deleteMany
/// returns the endpoint's count; a collection carries its paging. Scripts run over the anonymous STDIO path.
/// </summary>
public sealed class CodeModeSdkHonestySpec : IClassFixture<CodeModeSdkFixture>
{
    private readonly CodeModeSdkFixture _fx;

    public CodeModeSdkHonestySpec(CodeModeSdkFixture fx) => _fx = fx;

    private async Task<string> Run(string body)
    {
        var code = "try { " + body + " } catch (e) { SDK.Out.answer('threw: ' + e); }";
        var call = await _fx.CallToolAsync("koan_code_execute", new JObject { ["code"] = code });
        return McpHarnessFixtureBase.ContentText(call) ?? call.ToString();
    }

    private async Task<string> Seed(string entity, string tag)
    {
        var call = await _fx.CallToolAsync(_fx.ResolveToolName(entity, EntityEndpointOperationKind.Upsert),
            new JObject { ["model"] = new JObject { ["tag"] = tag } });
        return JObject.Parse(McpHarnessFixtureBase.ContentText(call)!)["id"]!.Value<string>()!;
    }

    [Fact]
    public async Task A_denied_delete_throws_and_the_row_remains()
    {
        var id = await Seed("keepsake", "kept");

        (await Run($"SDK.Out.answer('returned ' + SDK.Entities.keepsake.delete('{id}'));"))
            .Should().StartWith("threw:").And.Contain("unauthenticated");
        (await Run($"SDK.Out.answer(SDK.Entities.keepsake.getById('{id}').tag);")).Should().Be("kept");
    }

    [Fact]
    public async Task Deleting_a_missing_id_returns_zero()
        => (await Run("SDK.Out.answer('' + SDK.Entities.marble.delete('no-such-marble'));")).Should().Be("0");

    [Fact]
    public async Task Delete_many_returns_the_deleted_count()
    {
        var a = await Seed("marble", "dm-a");
        var b = await Seed("marble", "dm-b");
        var c = await Seed("marble", "dm-c");

        (await Run($"SDK.Out.answer('' + SDK.Entities.marble.deleteMany(['{a}', '{b}', '{c}', 'no-such-marble']));")).Should().Be("3");
    }

    [Fact]
    public async Task Denied_reads_throw_instead_of_reading_as_empty()
    {
        (await Run("SDK.Out.answer(JSON.stringify(SDK.Entities.coffer.collection()));")).Should().StartWith("threw:").And.Contain("unauthenticated");
        (await Run("SDK.Out.answer(JSON.stringify(SDK.Entities.coffer.getById('any')));")).Should().StartWith("threw:").And.Contain("unauthenticated");
    }

    [Fact]
    public async Task A_collection_carries_its_paging()
    {
        for (var i = 0; i < 4; i++) await Seed("marble", $"page-{i}");

        var text = await Run(
            "const p = SDK.Entities.marble.page(2, 2, { filter: JSON.stringify({ tag: { $in: ['page-0', 'page-1', 'page-2', 'page-3'] } }) });" +
            "SDK.Out.answer(JSON.stringify({ count: p.count, page: p.page, pageSize: p.pageSize, totalCount: p.totalCount }));");
        text.Should().StartWith("{", text);
        var answer = JObject.Parse(text);

        answer["count"]!.Value<int>().Should().Be(2);
        answer["page"]!.Value<int>().Should().Be(2);
        answer["pageSize"]!.Value<int>().Should().Be(2);
        answer["totalCount"]!.Value<int>().Should().Be(4);
    }
}
