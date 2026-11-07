using AwesomeAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>
/// WEB-0087: a declared entity controller's overrides are its tools, each answering as its route does, and taking what it declares: the
/// members it writes, its input, and nothing else. Every case is a real HTTP request and a real <c>tools/call</c>.
/// </summary>
public sealed class DeclaredOperationsSpec(ForumFixture forum) : IClassFixture<ForumFixture>
{
    private const string Mara = "mara";

    [Fact]
    public async Task Its_overrides_are_its_tools_and_nothing_else_of_it_is()
    {
        var names = (await forum.Tools(Mara)).Select(tool => tool["name"]!.Value<string>()!).ToArray();

        names.Should().Contain(["read_notice", "post_notice", "edit_notice"]);
        names.Should().NotContain(name => name.StartsWith("notice_", StringComparison.Ordinal), "the entity has no tools of its own on a declared controller");
        names.Should().NotContain(name => name.StartsWith("boards_notices", StringComparison.Ordinal), "an operation it does not override is no tool");
    }

    [Fact]
    public async Task Each_tool_takes_its_route_values_the_members_it_writes_and_its_input()
    {
        var tools = (await forum.Tools(Mara)).ToDictionary(tool => tool["name"]!.Value<string>()!);

        Fields(tools["post_notice"]).Should().BeEquivalentTo("id", "text", "where");
        tools["post_notice"]["inputSchema"]!["properties"]!["where"]!["description"]!.Value<string>().Should().Be("The board it goes on; left out, the general board.");
        Fields(tools["edit_notice"]).Should().BeEquivalentTo("notice", "text");
        Fields(tools["read_notice"]).Should().BeEquivalentTo("notice");
    }

    [Fact]
    public async Task A_create_answers_alike_on_both_doors_and_its_hook_reads_the_input()
    {
        var route = await forum.Route(HttpMethod.Post, "/boards/notices", new JObject { ["text"] = "by route", ["where"] = "lost" }, Mara);
        var tool = await forum.Tool("post_notice", new JObject { ["text"] = "by tool", ["where"] = "lost" }, Mara);

        route.Status.Should().Be(200, route.ToString());
        tool.Status.Should().Be(200, tool.ToString());
        foreach (var written in new[] { JObject.Parse(route.Text), JObject.Parse(tool.Text) })
        {
            written["board"]!.Value<string>().Should().Be("lost");
            written["author"]!.Value<string>().Should().Be(Mara);
        }
    }

    [Fact]
    public async Task A_member_outside_the_write_set_is_refused_on_both_doors()
    {
        var route = await forum.Route(HttpMethod.Post, "/boards/notices", new JObject { ["text"] = "forged", ["author"] = "sam" }, Mara);
        var tool = await forum.Tool("post_notice", new JObject { ["text"] = "forged", ["author"] = "sam" }, Mara);

        route.Status.Should().Be(400);
        tool.Status.Should().Be(400);
        route.Kind.Should().Be("invalid");
        tool.Kind.Should().Be("invalid");
        route.Text.Should().Be("author: This address does not write 'author'.");
        tool.Text.Should().Contain("This address does not write 'author'.");
    }

    [Fact]
    public async Task A_change_keeps_what_it_leaves_out_on_both_doors()
    {
        var posted = JObject.Parse((await forum.Route(HttpMethod.Post, "/boards/notices", new JObject { ["text"] = "first", ["where"] = "kept" }, Mara)).Text);
        var id = posted["id"]!.Value<string>()!;

        var route = await forum.Route(HttpMethod.Patch, $"/boards/notices/{id}", new JObject { ["text"] = "second" }, Mara);
        route.Status.Should().Be(200, route.ToString());
        var tool = await forum.Tool("edit_notice", new JObject { ["notice"] = id, ["text"] = "third" }, Mara);
        tool.Status.Should().Be(200, tool.ToString());

        var read = JObject.Parse((await forum.Tool("read_notice", new JObject { ["notice"] = id }, Mara)).Text);
        read["text"]!.Value<string>().Should().Be("third");
        read["board"]!.Value<string>().Should().Be("kept");
    }

    [Fact]
    public async Task A_partition_the_controller_does_not_serve_is_refused_on_both_doors()
    {
        var route = await forum.Route(HttpMethod.Get, "/boards/notices/n1?set=audit", null, Mara);
        route.Status.Should().Be(400);
        route.Text.Should().Be("set: This address serves no partition 'audit'.");

        // A controller with the whole surface refuses one too, on its routes and on the entity's tools.
        var articles = await forum.Route(HttpMethod.Get, "/data/articles?set=audit", null, Mara);
        var articleTool = await forum.Tool("article_collection", new JObject { ["set"] = "audit" }, Mara);
        articles.Status.Should().Be(400);
        articleTool.Status.Should().Be(400);
        articleTool.Failed.Should().BeTrue();
        articleTool.Text.Should().Contain("This address serves no partition 'audit'.");

        // The partition is read as the tool reads it, whatever its case or its kind of value.
        foreach (var (arguments, named) in new[] { (new JObject { ["Set"] = "audit" }, "audit"), (new JObject { ["set"] = 2024 }, "2024") })
        {
            var refused = await forum.Tool("article_collection", arguments, Mara);
            refused.Status.Should().Be(400, arguments.ToString());
            refused.Text.Should().Contain($"This address serves no partition '{named}'.");
        }
    }

    private static IEnumerable<string> Fields(JObject tool) => ((JObject)tool["inputSchema"]!["properties"]!).Properties().Select(field => field.Name);
}
