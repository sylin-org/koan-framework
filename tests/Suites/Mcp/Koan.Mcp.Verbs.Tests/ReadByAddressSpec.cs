using AwesomeAssertions;
using Koan.Mcp.Hosting;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>Addresses an agent reads that no tool names: the front page, a place, a redirect within the application and one away from it,
/// two redirects, a sign-in refusal with its challenge, and a write that never ran.</summary>
public sealed class ReadingController : ControllerBase
{
    [HttpGet("/")]
    public IActionResult Front() => Ok(new { front = true, reader = Forum.PersonId(User) });

    [HttpGet("/reading/places/{place}")]
    public IActionResult Place(string place) => Ok(new { place, reader = Forum.PersonId(User) });

    [HttpGet("/reading/old/{place}")]
    public IActionResult Moved(string place) => RedirectPermanent($"/reading/places/{place}");

    [HttpGet("/reading/twice")]
    public IActionResult Twice() => Redirect("/reading/old/open");

    [HttpGet("/reading/away")]
    public IActionResult Away() => Redirect("https://elsewhere.example/x");

    [HttpGet("/reading/challenge")]
    public IActionResult Challenged()
    {
        Response.Headers.WWWAuthenticate = "Proof realm=\"forum\"";
        return new Problem(StatusCodes.Status401Unauthorized, "Sign in to read here.", "sign-in").ToResult();
    }

    [HttpPost("/reading/busy"), Tool(Name = "busy_post")]
    public IActionResult Busy()
    {
        Response.Headers.RetryAfter = "7";
        return new Problem(StatusCodes.Status503ServiceUnavailable, "It never ran, so nothing was done and it is safe to send again.").ToResult();
    }
}

/// <summary>A host that reads by address, and tells a client what its provider composes.</summary>
public sealed class ReadingFixture : ForumFixture
{
    public const string Told = "Composed when asked: read any address here.";

    protected override Dictionary<string, string?> Settings() => new()
    {
        ["Koan:Mcp:ReadByAddress"] = "true",
        ["Koan:Mcp:Instructions"] = "A string in configuration, which the provider wins over.",
    };

    protected override void Compose(IServiceCollection services) => services.AddSingleton<IMcpInstructions, Composed>();

    private sealed class Composed : IMcpInstructions
    {
        public Task<string?> Instructions(CancellationToken ct) => Task.FromResult<string?>(Told);
    }
}

/// <summary>AI-0042: <c>read</c> answers what a GET of any address of the application answers, as its caller, through the whole pipeline; a
/// route tool takes an address in place of its route values; and <c>initialize</c> tells what the application's provider composes.</summary>
public sealed class ReadByAddressSpec(ReadingFixture host) : IClassFixture<ReadingFixture>
{
    private static JObject Address(string address) => new() { ["address"] = address };

    [Fact]
    public async Task With_no_address_it_reads_the_front_page_as_the_caller()
    {
        var result = await host.Call("read", new JObject(), "sam");

        result["isError"]?.Value<bool>().Should().NotBe(true, result.ToString());
        result["structuredContent"]!["front"]!.Value<bool>().Should().BeTrue();
        result["structuredContent"]!["reader"]!.Value<string>().Should().Be("sam");
        result["_meta"]!["address"]!.Value<string>().Should().Be(host.BaseUrl + "/");
    }

    [Fact]
    public async Task An_address_is_read_as_whoever_calls_whether_absolute_or_a_path()
    {
        var absolute = await host.Call("read", Address(host.BaseUrl + "/reading/places/open"), "mara");
        absolute["structuredContent"]!["reader"]!.Value<string>().Should().Be("mara", absolute.ToString());
        absolute["structuredContent"]!["place"]!.Value<string>().Should().Be("open");

        var visitor = await host.Call("read", Address("/reading/places/open#top"), null);
        visitor["structuredContent"]!["place"]!.Value<string>().Should().Be("open", visitor.ToString());
        visitor["structuredContent"]!["reader"].Should().BeNull("a caller with no credential is a visitor there as on the page");

        var tool = await host.Call("read", Address("/fidelity/names/ada"), "sam");
        tool["structuredContent"]!["name"]!.Value<string>().Should().Be("ada", "a route that is a tool is an address too");
    }

    [Fact]
    public async Task A_redirect_within_the_application_is_followed_once_and_one_away_is_answered_as_it_is()
    {
        var followed = await host.Call("read", Address("/reading/old/open"), "sam");
        followed["structuredContent"]!["place"]!.Value<string>().Should().Be("open", followed.ToString());
        followed["_meta"]!["address"]!.Value<string>().Should().Be(host.BaseUrl + "/reading/places/open");

        var twice = await host.Call("read", Address("/reading/twice"), "sam");
        twice["_meta"]!["status"]!.Value<int>().Should().Be(301, twice.ToString());
        twice["_meta"]!["location"]!.Value<string>().Should().EndWith("/reading/places/open");

        var away = await host.Call("read", Address("/reading/away"), "sam");
        away["isError"]?.Value<bool>().Should().NotBe(true);
        away["_meta"]!["status"]!.Value<int>().Should().Be(302, away.ToString());
        away["_meta"]!["location"]!.Value<string>().Should().Be("https://elsewhere.example/x");
    }

    [Theory]
    [InlineData("https://elsewhere.example/reading/places/open")]
    [InlineData("//elsewhere.example/reading/places/open")]
    public async Task Another_hosts_address_is_refused_with_a_problem_naming_the_field(string address)
    {
        var result = await host.Call("read", Address(address), "sam");

        result["isError"]!.Value<bool>().Should().BeTrue(result.ToString());
        result["_meta"]!["errorCode"]!.Value<string>().Should().Be("invalid");
        result["_meta"]!["problem"]!["errors"]!["address"]![0]!.Value<string>().Should().Contain("elsewhere.example");
        result["structuredContent"]!["status"]!.Value<int>().Should().Be(400);
    }

    [Fact]
    public async Task A_sign_in_refusal_carries_its_problem_and_the_challenge_the_route_sent()
    {
        var result = await host.Call("read", Address("/reading/challenge"), null);

        result["isError"]!.Value<bool>().Should().BeTrue(result.ToString());
        result["_meta"]!["status"]!.Value<int>().Should().Be(401);
        result["_meta"]!["challenge"]!.Value<string>().Should().Be("Proof realm=\"forum\"");
        result["_meta"]!["problem"]!["kind"]!.Value<string>().Should().Be("gated");
        result["_meta"]!["landed"].Should().BeNull("a read writes nothing, so it says nothing of what landed");
    }

    [Fact]
    public async Task An_address_that_names_nothing_answers_absent_and_a_dot_segment_is_resolved()
    {
        var nothing = await host.Call("read", Address("/reading/nowhere/at/all"), "sam");
        nothing["_meta"]!["status"]!.Value<int>().Should().Be(404, nothing.ToString());
        nothing["_meta"]!["problem"]!["kind"]!.Value<string>().Should().Be("absent");

        var resolved = await host.Call("read", Address("/reading/elsewhere/../places/open"), "sam");
        resolved["structuredContent"]!["place"]!.Value<string>().Should().Be("open", resolved.ToString());
    }

    [Fact]
    public async Task A_route_tool_takes_an_address_in_place_of_its_values_and_its_query()
    {
        var reply = (await host.Tools("sam")).Single(tool => tool["name"]!.Value<string>() == "reply");
        reply["inputSchema"]!["properties"]!["address"].Should().NotBeNull();
        reply["inputSchema"]!["required"].Should().BeNull("an address names place and topic at once");

        var written = await host.Call("reply", new JObject { ["address"] = host.BaseUrl + "/places/open/topics/t-open", ["body"] = "Named by its address." }, "sam");
        written["isError"]?.Value<bool>().Should().NotBe(true, written.ToString());
        written["_meta"]!["status"]!.Value<int>().Should().Be(201);
        written["_meta"]!["landed"]!.Value<bool>().Should().BeTrue();

        var census = await host.Call("places_census_get", new JObject { ["address"] = "/places/open/census?limit=3" }, "sam");
        census["structuredContent"]!["limit"]!.Value<int>().Should().Be(3, census.ToString());

        var disagrees = await host.Call("reply", new JObject { ["address"] = "/places/open/topics/t-open", ["place"] = "walled", ["body"] = "Which?" }, "sam");
        disagrees["_meta"]!["problem"]!["errors"]!["place"].Should().NotBeNull(disagrees.ToString());

        var unrelated = await host.Call("reply", new JObject { ["address"] = "/reading/places/open", ["body"] = "Where?" }, "sam");
        unrelated["_meta"]!["problem"]!["errors"]!["address"].Should().NotBeNull(unrelated.ToString());
        unrelated["_meta"]!["landed"]!.Value<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task A_write_that_never_ran_says_when_to_send_again_and_that_nothing_landed()
    {
        var result = await host.Call("busy_post", new JObject(), "sam");

        result["_meta"]!["status"]!.Value<int>().Should().Be(503, result.ToString());
        result["_meta"]!["retryAfter"]!.Value<string>().Should().Be("7");
        result["_meta"]!["landed"]!.Value<bool>().Should().BeFalse();

        var unknown = await host.Call("timeout_post", new JObject(), "sam");
        unknown["isError"]!.Value<bool>().Should().BeTrue(unknown.ToString());
        unknown["_meta"]!["landed"].Should().BeNull("a failure past the start may have done it");
    }

    [Fact]
    public async Task Read_is_listed_to_everyone_as_a_read_that_changes_nothing()
    {
        foreach (var caller in new[] { null, "sam" })
        {
            var read = (await host.Tools(caller)).Single(tool => tool["name"]!.Value<string>() == "read");
            read["annotations"]!["readOnlyHint"]!.Value<bool>().Should().BeTrue();
            read["annotations"]!["openWorldHint"]!.Value<bool>().Should().BeFalse();
            read["inputSchema"]!["properties"]!["address"]!["type"]!.Value<string>().Should().Be("string");
        }
    }

    [Fact]
    public async Task Initialize_tells_what_the_provider_composes()
    {
        var initialized = await host.Rpc("initialize", new JObject { ["protocolVersion"] = "2025-06-18" }, null);
        initialized["instructions"]!.Value<string>().Should().Be(ReadingFixture.Told);
    }
}
