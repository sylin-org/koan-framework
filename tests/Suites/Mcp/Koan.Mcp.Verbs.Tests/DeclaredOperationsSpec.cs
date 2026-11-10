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

        names.Should().Contain(["read_notices", "read_notice", "post_notice", "edit_notice"]);
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
        Fields(tools["read_notices"]).Should().BeEquivalentTo("on", "after", "size");
        tools["read_notices"]["inputSchema"]!["properties"]!["size"]!["description"]!.Value<string>().Should().Be("The most rows the page holds, at most 2.");
    }

    /// <summary>DATA-0170: the bound a declared collection's tool states is the one its page is clamped to, on both doors: the
    /// [Pagination] as the host's page policy reads it, not as it is written.</summary>
    [Fact]
    public async Task A_page_holds_at_most_the_bound_its_tool_states_on_both_doors()
    {
        var board = Guid.CreateVersion7().ToString("N");
        foreach (var text in new[] { "a", "b", "c" })
            (await forum.Route(HttpMethod.Post, "/boards/notices", new JObject { ["text"] = text, ["where"] = board }, Mara)).Status.Should().Be(201);

        var tool = (JObject)(await forum.Call("read_notices", new JObject { ["on"] = board, ["size"] = 3 }, Mara))["structuredContent"]!;
        var route = JObject.Parse((await forum.Route(HttpMethod.Get, $"/boards/notices?on={board}&size=3", null, Mara)).Text);

        foreach (var page in new[] { tool, route })
            page["items"]!.Select(notice => notice["text"]!.Value<string>()).Should().Equal(["a", "b"], "the tool states at most 2");
    }

    /// <summary>DATA-0170: a declared collection continues from the <c>next</c> its page answered, alike on both doors, and a cursor that
    /// does not open is refused naming <c>after</c>.</summary>
    [Fact]
    public async Task A_list_continues_from_the_next_its_page_answered_on_both_doors()
    {
        var board = Guid.CreateVersion7().ToString("N");
        foreach (var text in new[] { "c", "a", "b" })
            (await forum.Route(HttpMethod.Post, "/boards/notices", new JObject { ["text"] = text, ["where"] = board }, Mara)).Status.Should().Be(201);

        var first = await forum.Call("read_notices", new JObject { ["on"] = board }, Mara);
        var page = (JObject)first["structuredContent"]!;
        page["items"]!.Select(notice => notice["text"]!.Value<string>()).Should().Equal("a", "b");
        var next = page["next"]!.Value<string>()!;

        var tool = (JObject)(await forum.Call("read_notices", new JObject { ["on"] = board, ["after"] = next }, Mara))["structuredContent"]!;
        var route = JObject.Parse((await forum.Route(HttpMethod.Get, $"/boards/notices?on={board}&after={Uri.EscapeDataString(next)}", null, Mara)).Text);
        foreach (var last in new[] { tool, route })
        {
            last["items"]!.Select(notice => notice["text"]!.Value<string>()).Should().Equal("c");
            last["next"]!.Type.Should().Be(JTokenType.Null, "no notice follows the last page");
        }

        var refused = await forum.Tool("read_notices", new JObject { ["on"] = board, ["after"] = "not-a-cursor" }, Mara);
        refused.Status.Should().Be(400);
        refused.Text.Should().StartWith("after:");
    }

    [Fact]
    public async Task A_create_answers_alike_on_both_doors_and_its_hook_reads_the_input()
    {
        var route = await forum.Route(HttpMethod.Post, "/boards/notices", new JObject { ["text"] = "by route", ["where"] = "lost" }, Mara);
        var tool = await forum.Tool("post_notice", new JObject { ["text"] = "by tool", ["where"] = "lost" }, Mara);

        // WEB-0088: a create answers 201 at the row's address, on both doors.
        route.Status.Should().Be(201, route.ToString());
        tool.Status.Should().Be(201, tool.ToString());
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

    /// <summary>WEB-0089: a tool call tells its hooks the operation it serves, as its route does, and a refused one reads the problem: only
    /// a page's form, from a sender that reads pages, reads the page its controller hands.</summary>
    [Fact]
    public async Task A_tool_call_tells_its_hooks_the_operation_and_reads_the_problem_never_a_page()
    {
        var posted = await forum.Tool("post_notice", new JObject { ["text"] = "told by a tool" }, Mara);
        posted.Status.Should().Be(201, posted.ToString());
        var made = JObject.Parse(posted.Text);
        made["told"]!.Value<string>().Should().Be("Create");
        var edited = await forum.Tool("edit_notice", new JObject { ["notice"] = made["id"]!.Value<string>(), ["text"] = "told again" }, Mara);
        edited.Status.Should().Be(200, edited.ToString());
        JObject.Parse(edited.Text)["told"]!.Value<string>().Should().Be("Change");

        var tool = await forum.Tool("post_notice", new JObject { ["text"] = "forged", ["author"] = "sam" }, Mara);
        tool.Status.Should().Be(400);
        tool.Kind.Should().Be("invalid");
        tool.Word.Should().Be("invalid");
        tool.Text.Should().Contain("This address does not write 'author'.");

        using var browser = new HttpClient { BaseAddress = new Uri(forum.BaseUrl), Timeout = TimeSpan.FromSeconds(60) };
        browser.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Test " + Mara);
        browser.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html");
        using var form = await browser.PostAsync("/boards/notices",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = "forged", ["author"] = "sam" }), TestContext.Current.CancellationToken);
        form.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        var page = JObject.Parse(await form.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        page["operation"]!.Value<string>().Should().Be("Create");
        page["word"]!.Value<string>().Should().Be("invalid");
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
