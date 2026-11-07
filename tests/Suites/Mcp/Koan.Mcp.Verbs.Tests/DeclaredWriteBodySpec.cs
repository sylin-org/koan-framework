using AwesomeAssertions;
using Koan.Data.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>
/// K57, WEB-0088: a form's method is read only where it was sent to a declared change or delete, within the strictest limit of every route it
/// may reach there, so every other form route keeps its own; and a declared write's body that cannot bind is refused as Koan refuses, naming
/// the field, on both doors. Every case is a real HTTP request on a real port, and a real <c>tools/call</c>.
/// </summary>
public sealed class DeclaredWriteBodySpec(ForumFixture forum) : IClassFixture<ForumFixture>
{
    private const string Mara = "mara";

    private static readonly string TooMany = new('a', SignsController.Small * 4);

    [Fact]
    public async Task A_form_to_a_route_no_declared_controller_serves_keeps_its_own_limit()
    {
        var within = await forum.Form("/sign-letters", new Dictionary<string, string> { ["text"] = "Open" }, Mara);
        within.Status.Should().Be(200, within.Text);
        JObject.Parse(within.Text)["letters"]!.Value<int>().Should().Be(4);

        var over = await forum.Form("/sign-letters", new Dictionary<string, string> { ["text"] = TooMany }, Mara);
        over.Status.Should().Be(413, "the route's [RequestSizeLimit] holds though a declared controller is in the host");
    }

    [Fact]
    public async Task A_form_change_is_read_within_its_routes_limit()
    {
        var id = await PutUp("Open");

        var within = await forum.Form($"/signs/{id}", new Dictionary<string, string> { ["_method"] = "PATCH", ["text"] = "Closed" }, Mara);
        within.Status.Should().Be(303, within.Text);
        (await Text(id)).Should().Be("Closed");

        var over = await forum.Form($"/signs/{id}", new Dictionary<string, string> { ["_method"] = "PATCH", ["text"] = TooMany }, Mara);
        over.Status.Should().Be(413);
        over.Kind.Should().Be("invalid");
        (await Text(id)).Should().Be("Closed", "a change past its limit wrote nothing");
    }

    [Fact]
    public async Task A_body_that_cannot_bind_is_refused_naming_its_field_on_both_doors()
    {
        var id = await PutUp("Steady");
        var lost = "Lost " + Guid.CreateVersion7().ToString("N");

        foreach (var answer in new[]
                 {
                     await forum.Route(HttpMethod.Post, "/signs", new JArray(), Mara),
                     await forum.Route(HttpMethod.Patch, $"/signs/{id}", new JArray(), Mara),
                 })
        {
            answer.Status.Should().Be(400, answer.ToString());
            (answer.Kind, answer.Word).Should().Be(("invalid", "invalid"));
            answer.Text.Should().Be("A write is one JSON object of the members it writes.");
        }

        foreach (var (field, route, tool) in new[]
                 {
                     ("shape", await forum.Route(HttpMethod.Post, "/signs", new JObject { ["text"] = lost, ["shape"] = "Triangle" }, Mara),
                         await forum.Tool("put_up_sign", new JObject { ["text"] = lost, ["shape"] = "Triangle" }, Mara)),
                     ("frame", await forum.Route(HttpMethod.Post, "/signs", new JObject { ["text"] = lost, ["frame"] = "Triangle" }, Mara),
                         await forum.Tool("put_up_sign", new JObject { ["text"] = lost, ["frame"] = "Triangle" }, Mara)),
                     ("shape", await forum.Route(HttpMethod.Patch, $"/signs/{id}", new JObject { ["shape"] = "Triangle" }, Mara),
                         await forum.Tool("reshape_sign", new JObject { ["sign"] = id, ["shape"] = "Triangle" }, Mara)),
                 })
        {
            route.Status.Should().Be(400, route.ToString());
            tool.Status.Should().Be(400, tool.ToString());
            (route.Kind, route.Word).Should().Be(("invalid", "invalid"));
            (tool.Kind, tool.Word).Should().Be(("invalid", "invalid"));
            route.Text.Should().Be($"{field}: '{field}' cannot take this value.");
            tool.Text.Should().Contain($"'{field}' cannot take this value.");
        }

        using var app = forum.App();
        (await Sign.Query(sign => sign.Text == lost, TestContext.Current.CancellationToken)).Should().BeEmpty();
        (await Text(id)).Should().Be("Steady");
    }

    private async Task<string> PutUp(string text)
    {
        var made = await forum.Route(HttpMethod.Post, "/signs", new JObject { ["text"] = text, ["shape"] = "Round" }, Mara);
        made.Status.Should().Be(201, made.ToString());
        return JObject.Parse(made.Text)["id"]!.Value<string>()!;
    }

    private async Task<string?> Text(string id)
    {
        using var app = forum.App();
        return (await Sign.Get(id, TestContext.Current.CancellationToken))?.Text;
    }
}
