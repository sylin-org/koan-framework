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
        if (route.Kind is not null) tool.Kind.Should().Be(route.Kind);
        if (route.Text.Length > 0) tool.Text.Should().Be(route.Text);
        tool.Location.Should().Be(route.Location);
        tool.Gate.Should().Be(route.Gate);
        tool.Next.Should().Be(route.Next);
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
    public async Task An_absent_topic_and_a_hidden_topic_answer_alike_to_someone_signed_in_on_both_doors()
    {
        var cases = new (string Place, string Topic)[]
        {
            ("open", "t-nothing"),     // absent
            ("walled", "t-walled"),    // hidden from someone signed in
            ("walled", "t-nothing"),   // the same person, at an address that holds nothing
            ("nowhere", "t-open"),     // a reason worded differently
        };

        var answers = new List<Answer>();
        foreach (var (place, topic) in cases)
        {
            var route = await ReplyRoute(place, topic, Replying("into the void"), Sam);
            Alike(route, await ReplyTool(place, topic, Replying("into the void"), Sam));
            answers.Add(route);
        }

        answers.Distinct().Should().ContainSingle("nothing tells one of these from another: {0}", string.Join(" / ", answers));
        answers[0].Status.Should().Be(404);
        answers[0].Kind.Should().Be("absent");
        (await PostsWith("into the void")).Should().Be(0);
    }

    [Fact]
    public async Task A_visitor_to_a_private_place_is_told_to_sign_in_at_every_address_in_it_on_both_doors()
    {
        var there = await ReplyRoute("walled", "t-walled", Replying("hello?"), null);
        var nothing = await ReplyRoute("walled", "t-nothing", Replying("hello?"), null);

        Alike(there, await ReplyTool("walled", "t-walled", Replying("hello?"), null));
        Alike(nothing, await ReplyTool("walled", "t-nothing", Replying("hello?"), null));
        nothing.Should().Be(there, "nothing leaks of what the place holds");
        there.Status.Should().Be(401);
        there.Kind.Should().Be("gated");
        there.Gate.Should().Be("sign-in");
        there.Next.Should().Be("/sign-in");
        there.Text.Should().Be(Reply.SignIn.Text);
        (await PostsWith("hello?")).Should().Be(0);
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
        sealedRoute.Status.Should().Be(404);
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

    private static readonly string Keeper = "kit;role=keeper";

    private async Task<(Answer Route, Answer Tool)> Both(string path, string tool, string? user)
    {
        var route = await forum.Route(HttpMethod.Post, path, new JObject(), user);
        var called = await forum.Tool(tool, new JObject(), user);
        Alike(route, called);
        return (route, called);
    }

    [Theory]
    [InlineData("/shelf/own", "shelf_own_post")]
    [InlineData("/shelf/inherited", "shelf_inherited_post")]
    [InlineData("/ledger/inherited", "ledger_inherited_post")]
    public async Task What_a_controller_declares_holds_on_what_it_declares_and_what_it_inherits_on_both_doors(string path, string tool)
    {
        (await Both(path, tool, Sam)).Route.Status.Should().Be(403);
        (await Both(path, tool, null)).Route.Status.Should().Be(401);
        (await Both(path, tool, Keeper)).Route.Status.Should().Be(200);
    }

    [Fact]
    public async Task An_aspnet_policy_and_allow_anonymous_answer_on_both_doors()
    {
        (await Both("/vault/inherited", "vault_inherited_post", Keeper)).Route.Status.Should().Be(403, "the named policy runs, on the tool too");
        (await Both("/shelf/open", "shelf_open_post", null)).Route.Status.Should().Be(200);
    }

    [Theory]
    [InlineData("/stack/scope", "stack_scope", "kit;role=editor;scope=x", 403)]
    [InlineData("/stack/scope", "stack_scope", "kit;role=keeper", 403)]
    [InlineData("/stack/scope", "stack_scope", "kit;role=keeper;scope=x", 200)]
    [InlineData("/stack/roles", "stack_roles", "kit;role=editor", 403)]
    [InlineData("/stack/roles", "stack_roles", "kit;role=keeper", 403)]
    [InlineData("/stack/roles", "stack_roles", "kit;role=keeper;role=editor", 200)]
    [InlineData("/twice/scope", "twice_scope", "kit;scope=x", 403)]
    [InlineData("/twice/scope", "twice_scope", "kit;role=keeper", 403)]
    [InlineData("/twice/scope", "twice_scope", "kit;role=keeper;scope=x", 200)]
    [InlineData("/twice/role", "twice_role", "kit;role=editor", 403)]
    [InlineData("/twice/role", "twice_role", "kit;role=keeper;role=editor", 200)]
    public async Task A_class_and_a_method_both_hold_on_both_doors(string path, string tool, string user, int status)
        => (await Both(path, tool, user)).Route.Status.Should().Be(status);

    [Fact]
    public async Task The_list_hides_what_a_callers_claims_answer_and_keeps_what_a_policy_handler_would_decide()
    {
        var sam = (await forum.Tools(Sam)).Select(tool => tool["name"]!.Value<string>()).ToArray();
        var keeper = (await forum.Tools(Keeper)).Select(tool => tool["name"]!.Value<string>()).ToArray();

        sam.Should().NotContain("shelf_own_post").And.NotContain("shelf_inherited_post").And.NotContain("ledger_inherited_post").And.NotContain("stack_roles");
        sam.Should().Contain("shelf_open_post", "[AllowAnonymous] on the action");
        keeper.Should().Contain("shelf_own_post").And.Contain("shelf_inherited_post").And.Contain("ledger_inherited_post", "inherited actions are the controller's");
        keeper.Should().Contain("vault_inherited_post", "only its handler can say, and it may need the request");
        keeper.Should().NotContain("stack_roles", "the caller holds keeper and not editor");
    }

    [Fact]
    public async Task A_route_constraint_answers_as_it_does_on_http()
    {
        (await forum.Route(HttpMethod.Get, "/fidelity/files/..%2Fsecret", null, Sam)).Status.Should().Be(404);
        var tool = await forum.Tool("file_get", new JObject { ["name"] = "../secret" }, Sam);
        tool.Status.Should().Be(404, tool.ToString());
        tool.Kind.Should().Be("absent");
        Alike(await forum.Route(HttpMethod.Get, "/fidelity/files/good", null, Sam), await forum.Tool("file_get", new JObject { ["name"] = "good" }, Sam));
    }

    [Fact]
    public async Task Each_shape_of_input_an_action_takes_works_on_the_tool()
    {
        Alike(await forum.Route(HttpMethod.Get, "/fidelity/pages", null, Sam), await forum.Tool("page_get", new JObject(), Sam));
        Alike(await forum.Route(HttpMethod.Get, "/fidelity/pages/3", null, Sam), await forum.Tool("page_get", new JObject { ["page"] = 3 }, Sam));
        Alike(await forum.Route(HttpMethod.Get, "/fidelity/search?q=hello", null, Sam), await forum.Tool("search_get", new JObject { ["q"] = "hello" }, Sam));

        var many = await forum.Tool("many_post", new JObject { ["items"] = new JArray("a", "b") }, Sam);
        many.Status.Should().Be(200, many.ToString());
        JObject.Parse(many.Text)["count"]!.Value<int>().Should().Be(2);
        var text = await forum.Tool("text_post", new JObject { ["text"] = "hi" }, Sam);
        text.Status.Should().Be(200, text.ToString());
        JObject.Parse(text.Text)["text"]!.Value<string>().Should().Be("hi");

        var search = (await forum.Tools(Sam)).Single(tool => tool["name"]!.Value<string>() == "search_get");
        ((JObject)search["inputSchema"]!["properties"]!).Properties().Select(field => field.Name).Should().Equal("q");
    }

    [Fact]
    public async Task What_middleware_sets_reaches_the_action_and_a_tool_call_leaves_the_callers_request_as_it_was()
    {
        forum.Sent["X-Tenant"] = "acme";
        try
        {
            TenantMiddleware.AccessorAfterMcp = null;
            var route = await forum.Route(HttpMethod.Get, "/fidelity/tenant", null, Sam);
            var tool = await forum.Tool("tenant_get", new JObject(), Sam);

            Alike(route, tool);
            JObject.Parse(tool.Text)["scoped"]!.Value<string>().Should().Be("acme");
            JObject.Parse(tool.Text)["item"]!.Value<string>().Should().Be("acme");
            TenantMiddleware.AccessorAfterMcp.Should().Be("present");
        }
        finally
        {
            forum.Sent.Remove("X-Tenant");
        }
    }

    [Fact]
    public async Task A_rate_limit_on_a_route_binds_its_tool()
    {
        var route = await forum.Route(HttpMethod.Post, "/fidelity/limited", new JObject(), Sam);
        var tool = await forum.Tool("limited_post", new JObject(), Sam);

        route.Status.Should().Be(200, "the one permit of the window");
        tool.Status.Should().Be(429, "the same limit counted the route's call");
        tool.Failed.Should().BeTrue();
        (await forum.Route(HttpMethod.Post, "/fidelity/limited", new JObject(), Sam)).Status.Should().Be(429);
    }

    [Fact]
    public async Task A_failure_downstream_of_the_action_is_a_failed_call_and_names_nothing()
    {
        var tool = await forum.Tool("timeout_post", new JObject(), Sam);
        tool.Failed.Should().BeTrue();
        tool.Kind.Should().Be("failed");
        tool.Text.Should().NotContain("secret");
    }

    [Fact]
    public async Task A_rehearsal_is_gated_as_a_call_is()
    {
        var denied = await forum.Tool("admin_sweep_post", new JObject { ["dry_run"] = true }, Sam);
        denied.Failed.Should().BeTrue(denied.ToString());
        denied.Kind.Should().Be("gated");

        var asked = await forum.Tool("admin_sweep_post", new JObject { ["dry_run"] = true }, "root;role=admin");
        asked.Failed.Should().BeFalse(asked.ToString());
        asked.Text.Should().StartWith("Not executed.");

        var unprivileged = await forum.Tool("shelf_own_post", new JObject { ["dry_run"] = true }, Sam);
        unprivileged.Failed.Should().BeTrue("ASP.NET's declaration answers a rehearsal too: {0}", unprivileged);
    }

    [Fact]
    public async Task A_provider_can_answer_absent_as_a_missing_resource_does()
    {
        var hidden = await forum.Route(HttpMethod.Get, "/places/sealed/census", null, Sam);
        var missing = await forum.Route(HttpMethod.Get, "/places/nowhere/census", null, Sam);

        hidden.Should().Be(missing);
        hidden.Status.Should().Be(404);
        Alike(hidden, await forum.Tool("places_census_get", new JObject { ["place"] = "sealed" }, Sam));
        Alike(missing, await forum.Tool("places_census_get", new JObject { ["place"] = "nowhere" }, Sam));
    }

    [Theory]
    [InlineData("name_get", "name", "..")]
    [InlineData("name_get", "name", ".")]
    [InlineData("cat_get", "rest", "a/../../etc/passwd")]
    [InlineData("cat_get", "rest", "../secret")]
    public async Task A_dot_segment_never_reaches_an_action_as_http_cannot_deliver_one(string tool, string field, string value)
    {
        var answer = await forum.Tool(tool, new JObject { [field] = value }, Sam);

        answer.Status.Should().Be(404, answer.ToString());
        answer.Kind.Should().Be("absent");
        (await forum.Route(HttpMethod.Get, "/fidelity/names/%2E%2E", null, Sam)).Status.Should().Be(404);
    }

    [Fact]
    public async Task A_catch_all_keeps_its_slashes_and_a_dot_within_a_name_is_a_name()
    {
        Alike(await forum.Route(HttpMethod.Get, "/fidelity/cat/a/b.txt", null, Sam), await forum.Tool("cat_get", new JObject { ["rest"] = "a/b.txt" }, Sam));
        Alike(await forum.Route(HttpMethod.Get, "/fidelity/names/..x", null, Sam), await forum.Tool("name_get", new JObject { ["name"] = "..x" }, Sam));
    }

    [Fact]
    public async Task An_empty_route_value_is_refused_and_a_call_reaches_the_action_it_named()
    {
        var empty = await forum.Tool("item_archive", new JObject { ["id"] = "" }, Sam);
        empty.Status.Should().Be(400, empty.ToString());
        empty.Text.Should().StartWith("id:");

        var named = await forum.Tool("item_update", new JObject { ["id"] = "special" }, Sam);
        named.Status.Should().Be(200, named.ToString());
        JObject.Parse(named.Text)["ran"]!.Value<string>().Should().Be("update", "the action named, though the bare address is another action's");
        JObject.Parse((await forum.Route(HttpMethod.Post, "/fidelity/items/special", new JObject(), Sam)).Text)["ran"]!.Value<string>().Should().Be("special");

        var unmatched = await forum.Tool("item_archive", new JObject { ["id"] = "x" }, Sam);
        JObject.Parse(unmatched.Text)["ran"]!.Value<string>().Should().Be("archive");
    }

    [Fact]
    public async Task A_string_that_looks_like_a_date_reaches_the_action_as_sent_and_comes_back_as_it_was_answered()
    {
        const string instant = "2026-11-01T10:00:00+02:00";
        const string utc = "2026-11-01T10:00:00Z";

        Alike(await forum.Route(HttpMethod.Get, $"/fidelity/since?since={utc}&label={utc}", null, Sam),
            await forum.Tool("since_get", new JObject { ["since"] = utc, ["label"] = utc }, Sam));
        Alike(await forum.Route(HttpMethod.Get, $"/fidelity/at/{Uri.EscapeDataString(utc)}", null, Sam), await forum.Tool("at_get", new JObject { ["at"] = utc }, Sam));

        var body = await forum.RawBody("echo_post", new JObject { ["body"] = instant }, Sam);
        body.Should().Contain(instant, "the argument arrived as sent: {0}", body);
        var call = await forum.Call("echo_post", new JObject { ["body"] = instant }, Sam);
        call["content"]![0]!["text"]!.Value<string>().Should().Contain(instant);

        var stamp = await forum.RawBody("stamp_get", new JObject(), Sam);
        var parsed = new Newtonsoft.Json.JsonTextReader(new StringReader(stamp)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
        var result = JToken.ReadFrom(parsed)["result"]!;
        JToken.ReadFrom(new Newtonsoft.Json.JsonTextReader(new StringReader(result["content"]![0]!["text"]!.Value<string>()!)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })["at"]!.Value<string>().Should().Be(instant);
        result["structuredContent"]!["at"]!.Value<string>().Should().Be(instant);
    }

    [Fact]
    public async Task Only_an_object_is_structured_and_the_text_carries_the_rest()
    {
        var list = await forum.Call("list_get", new JObject(), Sam);
        list["content"]![0]!["text"]!.Value<string>().Should().Be("[1,2,3]");
        list["structuredContent"].Should().BeNull();

        var person = await forum.Call("whoami_get", new JObject(), Sam);
        person["structuredContent"]!["id"]!.Value<string>().Should().Be(Sam);
    }

    [Fact]
    public async Task A_redirect_says_where_it_went_in_the_text_and_the_structured_answer()
    {
        var sent = await forum.Call("places_visits_post", new JObject { ["place"] = "open" }, Mara);

        sent["isError"]?.Value<bool>().Should().NotBe(true);
        sent["content"]![0]!["text"]!.Value<string>().Should().Contain("/places/open");
        sent["structuredContent"]!["location"]!.Value<string>().Should().Be("/places/open");
        sent["structuredContent"]!["status"]!.Value<int>().Should().Be(303);
    }
}
