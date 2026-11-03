using AwesomeAssertions;
using Koan.Web.Attributes;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>Routes that say what they do, and routes that leave it to their method.</summary>
public sealed class StatedEffectsController : ControllerBase
{
    [HttpGet("/effects/read"), Tool(Name = "effects_read")]
    public IActionResult Read() => Ok();

    [HttpPost("/effects/post"), Tool(Name = "effects_post")]
    public IActionResult Post() => Ok();

    [HttpPut("/effects/put"), Tool(Name = "effects_put")]
    public IActionResult Put() => Ok();

    [HttpDelete("/effects/delete"), Tool(Name = "effects_delete")]
    public IActionResult Delete() => Ok();

    [HttpPost("/effects/ban"), Tool(Name = "effects_ban", Destructive = true, Idempotent = true)]
    public IActionResult Ban() => Ok();

    [HttpDelete("/effects/unsubscribe"), Tool(Name = "effects_unsubscribe", Destructive = false, Idempotent = false)]
    public IActionResult Unsubscribe() => Ok();

    [HttpPost("/effects/post-elsewhere"), Tool(Name = "effects_post_elsewhere", OpenWorld = true)]
    public IActionResult Elsewhere() => Ok();

    [HttpGet("/effects/read-claiming-destruction"), Tool(Name = "effects_read_claiming_destruction", Destructive = true, Idempotent = false)]
    public IActionResult ReadClaimingDestruction() => Ok();
}

/// <summary>A controller that speaks for its actions, and one action that speaks for itself.</summary>
[Tool(Destructive = true, OpenWorld = true)]
public sealed class StatedForAllController : ControllerBase
{
    [HttpPost("/effects/all/one")]
    public IActionResult One() => Ok();

    [HttpPost("/effects/all/two"), Tool(Name = "effects_all_own_word", Destructive = false, OpenWorld = false)]
    public IActionResult Two() => Ok();
}

/// <summary>A tool says what it does: what it states first, then what its method promises, and nothing the framework cannot know, which the specification reads as "it may".</summary>
public sealed class ToolAnnotationsSpec(ForumFixture host) : IClassFixture<ForumFixture>
{
    private async Task<JObject> Annotations(string tool)
        => (JObject)(await host.Tools("mara")).Single(item => item["name"]!.Value<string>() == tool)["annotations"]!;

    [Theory]
    [InlineData("effects_read", true, false, true, false)]
    [InlineData("effects_post", false, null, false, null)]
    [InlineData("effects_put", false, null, true, null)]
    [InlineData("effects_delete", false, true, true, null)]
    [InlineData("effects_ban", false, true, true, null)]
    [InlineData("effects_unsubscribe", false, false, false, null)]
    [InlineData("effects_post_elsewhere", false, null, false, true)]
    [InlineData("effects_read_claiming_destruction", true, false, true, false)]
    [InlineData("effects_all_one_post", false, true, false, true)]
    [InlineData("effects_all_own_word", false, false, false, false)]
    public async Task A_route_says_what_it_states_and_what_its_method_promises_and_nothing_else(string tool, bool readOnly, bool? destructive, bool idempotent, bool? openWorld)
    {
        var annotations = await Annotations(tool);

        annotations["readOnlyHint"]!.Value<bool>().Should().Be(readOnly);
        (annotations["destructiveHint"] is { } said ? said.Value<bool>() : (bool?)null).Should().Be(destructive, "a hint not known is left out, not guessed false");
        annotations["idempotentHint"]!.Value<bool>().Should().Be(idempotent);
        (annotations["openWorldHint"] is { } open ? open.Value<bool>() : (bool?)null).Should().Be(openWorld, "a hint not known is left out, not guessed false");
    }

    /// <summary>What a client's permission prompt shows, where neither a route nor a paragraph helps the person reading it.</summary>
    [Theory]
    [InlineData("effects_post", "Effects post")]
    [InlineData("effects_post_elsewhere", "Effects post elsewhere")]
    [InlineData("reply", "Reply")]
    public async Task A_person_reads_a_title_made_from_the_name(string tool, string title)
        => (await Annotations(tool))["title"]!.Value<string>().Should().Be(title);

    /// <summary>A route offers no rehearsal: all it could say is that it cannot, on every mutating tool an agent is shown.</summary>
    [Fact]
    public async Task A_route_does_not_offer_what_it_cannot_do()
    {
        var post = (await host.Tools("mara")).Single(item => item["name"]!.Value<string>() == "effects_post");

        ((JObject)post["inputSchema"]!["properties"]!).ContainsKey("dry_run").Should().BeFalse();
    }

    /// <summary>A client that asks anyway, from an old list or a habit, must not have the action run for real.</summary>
    [Fact]
    public async Task A_rehearsal_asked_for_anyway_still_runs_nothing()
    {
        var answer = await host.Tool("effects_post", new JObject { ["dry_run"] = true }, "mara");

        answer.Text.Should().Contain("Not executed");
    }
}
