using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Koan.Mcp.Hosting;
using Koan.Mcp.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>ARCH-0142: every tool name a client is listed is valid for Claude's Messages API, whichever door made it, and a result and a list are the specification's shape.</summary>
public sealed partial class ToolNameWireSpec : IClassFixture<ConformanceFixture>
{
    private readonly ConformanceFixture _fx;

    public ToolNameWireSpec(ConformanceFixture fx) => _fx = fx;

    /// <summary>The <c>name</c> of a Tool in Claude's Messages API, as its reference states it.</summary>
    [GeneratedRegex("^[a-zA-Z0-9_-]{1,128}$")]
    private static partial Regex ClaudeAccepts();

    [Fact]
    public async Task Every_listed_tool_has_a_name_Claude_accepts_and_the_one_rule()
    {
        var names = (await _fx.ListToolsAsync()).Select(tool => tool["name"]!.Value<string>()!).ToArray();

        names.Should().NotBeEmpty();
        names.Should().AllSatisfy(name =>
        {
            ClaudeAccepts().IsMatch(name).Should().BeTrue($"'{name}' is a name Claude's API takes");
            name.Should().MatchRegex("^[a-z0-9_]{1,64}$");
        });
    }

    [Fact]
    public async Task An_entity_tool_is_its_entity_and_its_operation_in_words()
        => (await _fx.GetWireToolAsync("gadget_get_by_id")).Should().NotBeNull();

    [Fact]
    public async Task A_list_says_no_next_page_by_leaving_the_field_out()
    {
        using var scope = _fx.Services.CreateScope();
        var response = await scope.ServiceProvider.GetRequiredService<McpServer>().CreateHandler().ListTools(TestContext.Current.CancellationToken);
        var wire = JObject.Parse(JsonConvert.SerializeObject(response));

        wire.ContainsKey("next").Should().BeFalse("the specification has no such field");
        wire.ContainsKey("nextCursor").Should().BeFalse("nothing follows this page");
    }

    [Fact]
    public async Task A_results_metadata_is_the_specifications_field()
    {
        var call = await _fx.CallToolAsync("gadget_new", new JObject());

        call["_meta"].Should().NotBeNull();
        call["meta"].Should().BeNull("the specification has no such field");
    }
}
