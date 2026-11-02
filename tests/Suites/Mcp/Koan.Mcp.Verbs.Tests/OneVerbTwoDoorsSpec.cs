using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Koan.Mcp.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>
/// ARCH-0141: one definition answers as a route and as a tool. Every case drives a real HTTP request and a real <c>tools/call</c>,
/// and asserts they answer alike and have the same effect.
/// </summary>
public sealed class OneVerbTwoDoorsSpec(ForumFixture forum) : IClassFixture<ForumFixture>
{
    private const string Mara = "mara";
    private const string Sam = "sam";
    private const string Steward = "steward";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JObject Replying(string body, object? extra = null)
    {
        var fields = new JObject { ["body"] = body };
        if (extra is not null) fields.Merge(JObject.FromObject(extra));
        return fields;
    }

    private Task<Answer> ReplyRoute(string place, string topic, JObject fields, string? user)
        => forum.Route(HttpMethod.Post, $"/places/{place}/topics/{topic}/replies", fields, user);

    private Task<Answer> ReplyTool(string place, string topic, JObject fields, string? user)
    {
        var arguments = (JObject)fields.DeepClone();
        arguments["place"] = place;
        arguments["topic"] = topic;
        return forum.Tool("reply", arguments, user);
    }

    private Task<Answer> RoleRoute(string person, JObject fields, string? user)
        => forum.Route(HttpMethod.Post, $"/people/{person}/roles", fields, user);

    private Task<Answer> RoleTool(string person, JObject fields, string? user)
    {
        var arguments = (JObject)fields.DeepClone();
        arguments["person"] = person;
        return forum.Tool("change_role", arguments, user);
    }

    private static void Alike(Answer route, Answer tool)
    {
        tool.Status.Should().Be(route.Status, "route said {0}, tool said {1}", route, tool);
        tool.Failed.Should().Be(route.Failed);
        tool.Kind.Should().Be(route.Kind);
        tool.Text.Should().Be(route.Text);
        tool.Location.Should().Be(route.Location);
    }

    private async Task<int> PostsWith(string body)
    {
        using var app = forum.App();
        return (await Post.Query(post => post.Body == body, Ct)).Count;
    }

    private async Task<IReadOnlyList<string>> LogOf(string what)
    {
        using var app = forum.App();
        return (await LogEntry.Query(entry => entry.What == what, Ct)).Select(entry => entry.Actor).ToArray();
    }

    private async Task<bool> Holds(string person, string role)
    {
        using var app = forum.App();
        return (await forum.Services.GetRequiredService<RoleCollection>().Bag(person, true, Ct)).InRole(role);
    }

    [Fact]
    public async Task A_reply_lands_the_same_on_either_door_with_its_author_from_the_principal_and_an_entry_in_its_commit()
    {
        var route = await ReplyRoute("open", "t-open", Replying("through the route"), Mara);
        var tool = await ReplyTool("open", "t-open", Replying("through the tool"), Mara);

        route.Status.Should().Be(201, route.ToString());
        tool.Status.Should().Be(201, tool.ToString());
        tool.Failed.Should().BeFalse();
        JObject.Parse(tool.Text).Properties().Select(p => p.Name).Should().BeEquivalentTo(JObject.Parse(route.Text).Properties().Select(p => p.Name));

        foreach (var (answer, body) in new[] { (route, "through the route"), (tool, "through the tool") })
        {
            var written = JObject.Parse(answer.Text);
            written["authorId"]!.Value<string>().Should().Be(Mara);
            written["topicId"]!.Value<string>().Should().Be("t-open");
            written["body"]!.Value<string>().Should().Be(body);
            (await LogOf($"reply:{written["id"]}")).Should().Equal(Mara);
        }
    }

    [Fact]
    public async Task An_absent_topic_a_hidden_topic_and_a_visitor_to_a_private_place_answer_alike_on_both_doors()
    {
        var cases = new (string Place, string Topic, string? User)[]
        {
            ("open", "t-nothing", Sam),      // absent
            ("walled", "t-walled", Sam),     // hidden from someone signed in
            ("walled", "t-walled", null),    // an anonymous visitor to a private place
            ("walled", "t-nothing", null),   // the same visitor, at an address that holds nothing
            ("nowhere", "t-open", Sam),      // a reason worded differently
        };

        var answers = new List<Answer>();
        foreach (var (place, topic, user) in cases)
        {
            var route = await ReplyRoute(place, topic, Replying("into the void"), user);
            var tool = await ReplyTool(place, topic, Replying("into the void"), user);
            Alike(route, tool);
            answers.Add(route);
        }

        answers.Distinct().Should().ContainSingle("nothing tells one of these from another: {0}", string.Join(" / ", answers));
        answers[0].Status.Should().Be(404);
        answers[0].Kind.Should().Be("absent");
        (await PostsWith("into the void")).Should().Be(0);
    }

    [Fact]
    public async Task A_forged_author_is_ignored_on_both_doors()
    {
        var fields = Replying("signed by someone else", new { authorId = Sam, author = Sam, AuthorId = Sam });
        var route = await ReplyRoute("open", "t-open", fields, Mara);
        var tool = await ReplyTool("open", "t-open", fields, Mara);

        JObject.Parse(route.Text)["authorId"]!.Value<string>().Should().Be(Mara);
        JObject.Parse(tool.Text)["authorId"]!.Value<string>().Should().Be(Mara);
        Alike(route, tool);
    }

    [Fact]
    public async Task A_body_cannot_name_the_address()
    {
        var fields = Replying("aimed elsewhere", new { topic = "t-walled", place = "walled" });
        var route = await ReplyRoute("open", "t-open", fields, Sam);

        route.Status.Should().Be(201, route.ToString());
        JObject.Parse(route.Text)["topicId"]!.Value<string>().Should().Be("t-open");
    }

    [Fact]
    public async Task A_value_that_cannot_bind_is_refused_with_its_field_named_on_both_doors()
    {
        var replyRoute = await ReplyRoute("open", "t-open", Replying("badly typed", new { notify = "perhaps" }), Mara);
        var replyTool = await ReplyTool("open", "t-open", Replying("badly typed", new { notify = "perhaps" }), Mara);
        var roleRoute = await RoleRoute(Sam, new JObject { ["role"] = Forum.Member, ["give"] = "banana" }, Steward);
        var roleTool = await RoleTool(Sam, new JObject { ["role"] = Forum.Member, ["give"] = "banana" }, Steward);

        Alike(replyRoute, replyTool);
        Alike(roleRoute, roleTool);
        replyRoute.Status.Should().Be(400);
        replyRoute.Kind.Should().Be("invalid");
        replyRoute.Text.Should().Contain("notify", because: replyRoute.ToString());
        roleRoute.Text.Should().Contain("give", because: roleRoute.ToString());
        (await PostsWith("badly typed")).Should().Be(0);
        (await Holds(Sam, Forum.Member)).Should().BeFalse();
    }

    [Fact]
    public async Task A_required_field_left_out_is_refused_with_its_name_on_both_doors()
    {
        var route = await RoleRoute(Sam, new JObject { ["give"] = true }, Steward);
        var tool = await RoleTool(Sam, new JObject { ["give"] = true }, Steward);

        Alike(route, tool);
        route.Status.Should().Be(400);
        route.Text.Should().Contain("Role", because: route.ToString());
    }

    [Fact]
    public async Task A_retried_call_makes_one_row_on_either_door_and_across_them()
    {
        var first = await ReplyTool("open", "t-open", Replying("said once"), Mara);
        var again = await ReplyTool("open", "t-open", Replying("said once"), Mara);
        var byRoute = await ReplyRoute("open", "t-open", Replying("said once"), Mara);
        var chosen = await ReplyRoute("open", "t-open", Replying("said twice", new { id = "chosen-id" }), Mara);
        var chosenAgain = await ReplyTool("open", "t-open", Replying("said twice", new { id = "chosen-id" }), Mara);

        first.Status.Should().Be(201, first.ToString());
        again.Status.Should().Be(201, again.ToString());
        Alike(byRoute, again);
        Alike(chosen, chosenAgain);
        (await PostsWith("said once")).Should().Be(1);
        (await PostsWith("said twice")).Should().Be(1);

        var conflict = await ReplyRoute("open", "t-open", Replying("other words", new { id = "chosen-id" }), Mara);
        var conflictTool = await ReplyTool("open", "t-open", Replying("other words", new { id = "chosen-id" }), Mara);
        Alike(conflict, conflictTool);
        conflict.Status.Should().Be(409);
        conflict.Kind.Should().Be("conflict");
    }

    [Fact]
    public async Task A_gated_caller_is_told_why_on_both_doors()
    {
        var locked = (await ReplyRoute("open", "t-locked", Replying("too late"), Mara), await ReplyTool("open", "t-locked", Replying("too late"), Mara));
        var visitor = (await ReplyRoute("open", "t-open", Replying("who am I"), null), await ReplyTool("open", "t-open", Replying("who am I"), null));
        var notSteward = (await RoleRoute(Sam, new JObject { ["role"] = Forum.Member }, Mara), await RoleTool(Sam, new JObject { ["role"] = Forum.Member }, Mara));
        var anonymous = (await RoleRoute(Sam, new JObject { ["role"] = Forum.Member }, null), await RoleTool(Sam, new JObject { ["role"] = Forum.Member }, null));

        Alike(locked.Item1, locked.Item2);
        locked.Item1.Status.Should().Be(403);
        locked.Item1.Kind.Should().Be("gated");
        locked.Item1.Text.Should().Be(Reply.Locked.Text);

        Alike(visitor.Item1, visitor.Item2);
        visitor.Item1.Status.Should().Be(401);
        visitor.Item1.Text.Should().Be(Reply.SignIn.Text);

        Alike(notSteward.Item1, notSteward.Item2);
        notSteward.Item1.Status.Should().Be(403);
        notSteward.Item1.Text.Should().Be(ChangeRole.NotYours.Text);

        Alike(anonymous.Item1, anonymous.Item2);
        anonymous.Item1.Status.Should().Be(401, "the verb's own gate answers before anything is read");

        (await Holds(Sam, Forum.Member)).Should().BeFalse();
    }

    [Fact]
    public async Task A_role_change_commits_with_its_entry_on_either_door()
    {
        var route = await RoleRoute("mara", new JObject { ["role"] = Forum.Member }, Steward);
        (await Holds("mara", Forum.Member)).Should().BeTrue(route.ToString());
        var taken = await RoleTool("mara", new JObject { ["role"] = Forum.Member, ["give"] = false }, Steward);

        route.Status.Should().Be(200, route.ToString());
        taken.Status.Should().Be(200, taken.ToString());
        (await Holds("mara", Forum.Member)).Should().BeFalse();
        (await LogOf($"role:{Forum.Member}:mara")).Should().Equal(Steward, Steward);
    }

    [Fact]
    public async Task A_failed_or_refused_role_change_leaves_no_role_and_no_entry_on_either_door()
    {
        foreach (var (person, role, status, kind) in new[]
                 {
                     ("sam", Forum.Doomed, 500, "failed"),        // throws after the role is added
                     ("ice", Forum.Member, 409, "conflict"),      // refuses after the role is added
                 })
        {
            var fields = new JObject { ["role"] = role };
            var route = await RoleRoute(person, fields, Steward);
            var tool = await RoleTool(person, fields, Steward);

            Alike(route, tool);
            route.Status.Should().Be(status, route.ToString());
            route.Kind.Should().Be(kind);
            (await Holds(person, role)).Should().BeFalse();
            (await LogOf($"role:{role}:{person}")).Should().BeEmpty();
        }

        // Nothing about the cause crosses either door.
        (await RoleTool("sam", new JObject { ["role"] = Forum.Doomed }, Steward)).Text.Should().NotContain("fell over");
    }

    [Fact]
    public async Task A_rule_spanning_actor_target_and_role_answers_alike()
    {
        foreach (var (person, role, actor, status) in new[]
                 {
                     ("nobody", Forum.Member, Steward, 404),   // the target
                     ("sam", "role:invented", Steward, 400),   // the role
                     ("steward", Forum.Member, Steward, 403),  // the actor is the target
                 })
        {
            var fields = new JObject { ["role"] = role };
            var route = await RoleRoute(person, fields, actor);
            Alike(route, await RoleTool(person, fields, actor));
            route.Status.Should().Be(status, route.ToString());
        }
    }

    [Fact]
    public async Task The_tool_list_hides_what_is_knowable_and_keeps_what_only_a_resource_decides()
    {
        string[] Names(JObject[] tools) => tools.Select(tool => tool["name"]!.Value<string>()!).ToArray();

        var visitor = Names(await forum.Tools(null));
        var person = Names(await forum.Tools(Sam));
        var admin = Names(await forum.Tools("root;role=admin"));
        var flagged = Names(await forum.Tools("sly;flagged=true"));

        visitor.Should().Contain("reply", "only the Topic addressed can say whether a visitor may reply");
        visitor.Should().NotContain("change_role", "the verb's own gate needs a signed-in caller, and no resource changes that");
        person.Should().Contain("change_role", "who may change which role only the command knows");
        person.Should().NotContain("admin_sweep_post", "a role the caller's claims do not hold");
        admin.Should().Contain("admin_sweep_post");
        flagged.Should().NotContain("places_census_get", "the application's own rule answers a list with no resource");
        person.Should().Contain("places_census_get");
    }

    [Fact]
    public async Task A_hidden_tool_answers_a_direct_call_as_its_route_does()
    {
        var route = await forum.Route(HttpMethod.Post, "/admin/sweep", new JObject(), Sam);
        var tool = await forum.Tool("admin_sweep_post", new JObject(), Sam);
        Alike(route, tool);
        route.Status.Should().Be(403);

        var allowedRoute = await forum.Route(HttpMethod.Post, "/admin/sweep", new JObject(), "root;role=admin");
        var allowedTool = await forum.Tool("admin_sweep_post", new JObject(), "root;role=admin");
        Alike(allowedRoute, allowedTool);
        allowedRoute.Status.Should().Be(200);
    }

    [Fact]
    public async Task The_applications_own_rule_answers_a_call_per_resource()
    {
        var open = await forum.Route(HttpMethod.Get, "/places/open/census?limit=3", null, Sam);
        var openTool = await forum.Tool("places_census_get", new JObject { ["place"] = "open", ["limit"] = 3 }, Sam);
        var sealedRoute = await forum.Route(HttpMethod.Get, "/places/sealed/census", null, Sam);
        var sealedTool = await forum.Tool("places_census_get", new JObject { ["place"] = "sealed" }, Sam);

        open.Status.Should().Be(200, open.ToString());
        openTool.Status.Should().Be(200, openTool.ToString());
        JObject.Parse(openTool.Text)["limit"]!.Value<int>().Should().Be(3, "a query field reached the action from the tool's arguments");
        Alike(sealedRoute, sealedTool);
        sealedRoute.Status.Should().Be(403);
    }

    [Fact]
    public async Task A_form_and_a_json_body_are_the_same_input()
    {
        var form = await forum.Form("/places/open/topics/t-open/replies", new() { ["body"] = "as a form", ["parent"] = "someone" }, Mara);
        var unreadable = await forum.Form("/places/open/topics/t-open/replies", new() { ["body"] = "bad form", ["notify"] = "perhaps" }, Mara);

        form.Status.Should().Be(201, form.ToString());
        JObject.Parse(form.Text)["parentId"]!.Value<string>().Should().Be("someone");
        unreadable.Text.Should().Contain("notify");
    }

    [Fact]
    public async Task A_tool_says_what_its_input_is_from_the_one_definition()
    {
        var tools = await forum.Tools(Steward);
        var reply = tools.Single(tool => tool["name"]!.Value<string>() == "reply");
        var input = (JObject)reply["inputSchema"]!["properties"]!;

        reply["description"]!.Value<string>().Should().Be("Reply in a Topic.");
        input.Properties().Select(p => p.Name).Should().BeEquivalentTo("place", "topic", "body", "parent", "notify", "id", "dry_run");
        input["body"]!["description"]!.Value<string>().Should().Be("The reply, in plain words.");
        input["notify"]!["type"]!.Value<string>().Should().Be("boolean");
        reply["inputSchema"]!["required"]!.Values<string>().Should().BeEquivalentTo("place", "topic");
        input.Properties().Select(p => p.Name).Should().NotContain("authorId");

        var role = tools.Single(tool => tool["name"]!.Value<string>() == "change_role");
        role["inputSchema"]!["required"]!.Values<string>().Should().BeEquivalentTo("person", "role");

        var read = tools.Single(tool => tool["name"]!.Value<string>() == "people_roles_of");
        read["annotations"]!["readOnlyHint"]!.Value<bool>().Should().BeTrue();
    }

    private async Task<JToken> Stdio(string name, JObject arguments)
    {
        using var scope = forum.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<McpServer>().CreateHandler();
        var result = await handler.CallTool(new McpRpcHandler.ToolsCallParams { Name = name, Arguments = arguments }, Ct);
        return JToken.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(result));
    }

    [Fact]
    public async Task Over_stdio_a_route_tool_answers_with_no_request_around_it_and_origin_local_admits_only_stdio()
    {
        var visitor = await Stdio("reply", Replying("from the pipe", new { place = "open", topic = "t-open" }));
        visitor["isError"]!.Value<bool>().Should().BeTrue(visitor.ToString());
        visitor["content"]![0]!["text"]!.Value<string>().Should().Be(Reply.SignIn.Text);

        var local = await Stdio("local_check_post", new JObject());
        local["isError"]?.Value<bool>().Should().NotBe(true, local.ToString());

        (await forum.Route(HttpMethod.Post, "/local/check", new JObject(), Mara)).Status.Should().Be(403);
        var remote = await forum.Tool("local_check_post", new JObject(), Mara);
        remote.Status.Should().Be(403, "a tool call over HTTP keeps the origin it came from");
    }

    [Fact]
    public async Task What_success_answers_stays_the_actions_and_the_tool_reports_it()
    {
        var route = await forum.Route(HttpMethod.Post, "/places/open/visits", new JObject(), Mara);
        var tool = await forum.Tool("places_visits_post", new JObject { ["place"] = "open" }, Mara);

        route.Status.Should().Be(303, route.ToString());
        route.Location.Should().Be("/places/open");
        Alike(route, tool);
    }

    [Fact]
    public async Task A_delete_carries_its_body_and_its_query_where_the_action_takes_them()
    {
        var tool = await forum.Tool("places_visits_forget", new JObject { ["place"] = "open", ["role"] = "gone", ["count"] = 2 }, Mara);
        tool.Status.Should().Be(200, tool.ToString());
        var said = JObject.Parse(tool.Text);
        said["count"]!.Value<int>().Should().Be(2);
        said["role"]!.Value<string>().Should().Be("gone");
    }

    [Fact]
    public async Task A_rejected_credential_is_not_a_visitor_on_either_door()
    {
        var route = await ReplyRoute("open", "t-open", Replying("from a bad token"), "rejected");
        var tool = await ReplyTool("open", "t-open", Replying("from a bad token"), "rejected");

        Alike(route, tool);
        route.Status.Should().Be(401);
        route.Text.Should().NotBe(Reply.SignIn.Text, "the gate answered before the command was sent");
    }

    [Fact]
    public async Task A_rehearsal_runs_nothing()
    {
        var rehearsal = await forum.Tool("reply", Replying("only rehearsed", new { place = "open", topic = "t-open", dry_run = true }), Mara);

        rehearsal.Failed.Should().BeFalse();
        rehearsal.Text.Should().StartWith("Not executed.");
        (await PostsWith("only rehearsed")).Should().Be(0);
    }

    [Fact]
    public async Task A_tool_controller_over_an_entity_exposes_its_operations_and_its_own_actions()
    {
        var names = (await forum.Tools(Mara)).Select(tool => tool["name"]!.Value<string>()!).ToArray();
        names.Should().Contain("article.collection", "the entity's operations are the entity's own tools");
        names.Should().Contain("article.upsert");
        names.Should().Contain("data_articles_words_count", "the route with the particle in lower-case words");
        names.Should().Contain("data_articles_retitle_post");

        var upsert = await forum.Tool("article.upsert", new JObject { ["model"] = new JObject { ["id"] = "a1", ["title"] = "two words" } }, Mara);
        upsert.Failed.Should().BeFalse(upsert.ToString());

        var route = await forum.Route(HttpMethod.Get, "/data/articles/a1/words", null, Mara);
        var tool = await forum.Tool("data_articles_words_count", new JObject { ["id"] = "a1" }, Mara);
        Alike(route, tool);
        JObject.Parse(route.Text)["words"]!.Value<int>().Should().Be(2);

        var missingRoute = await forum.Route(HttpMethod.Get, "/data/articles/nope/words", null, Mara);
        var missingTool = await forum.Tool("data_articles_words_count", new JObject { ["id"] = "nope" }, Mara);
        missingRoute.Status.Should().Be(404);
        missingTool.Status.Should().Be(404);
        missingTool.Kind.Should().Be("absent");
    }

    [Fact]
    public async Task A_body_bound_by_an_api_controller_refuses_a_bad_value_with_its_field_named_on_both_doors()
    {
        var fields = new JObject { ["title"] = "new", ["tries"] = "several" };
        var route = await forum.Route(HttpMethod.Post, "/data/articles/a2/retitle", fields, Mara);
        var arguments = (JObject)fields.DeepClone();
        arguments["id"] = "a2";
        var tool = await forum.Tool("data_articles_retitle_post", arguments, Mara);

        route.Status.Should().Be(400, route.ToString());
        tool.Status.Should().Be(400, tool.ToString());
        tool.Kind.Should().Be("invalid");
        route.Text.Should().Contain("tries", because: route.ToString());
        tool.Text.Should().Contain("tries", because: tool.ToString());
    }
}
