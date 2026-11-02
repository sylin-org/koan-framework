using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>A route marked as a tool answers to nothing it did not already answer to when the MCP module is absent, and this assembly does
/// not reference it. A command here runs on a route that cannot hold, so it never runs (DATA-0167).</summary>
[Collection(RestEntityCollection.Name)]
public sealed class ToolRouteSpec(RestEntityWebFactory fx)
{
    public sealed record WidgetInput(string? Name, int Count = 1);

    public sealed record Never(string Why) : ICommand<string>
    {
        public Task<Outcome<string>> Run(CancellationToken ct) => Task.FromResult<Outcome<string>>(new Outcome<string>.Done(Why));
    }

    /// <summary>Without its [Tool], or nothing in this assembly could boot: a tool with a named policy stops the host at start.</summary>
    public sealed class NamedPolicy
    {
        [Authorize(Policy = "a-named-one")]
        public void Verb() { }
    }

    public sealed class ScopedOnItsClass
    {
        [Access(all: "has:scope:widgets:write")]
        public sealed class Inner
        {
            public void Plain() { }

            [RequireScope("widgets:read")]
            public void Own() { }
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpStatusCode Status, JsonElement Body)> Post(string path, HttpContent? content = null)
    {
        using var response = await fx.Client.PostAsync(path, content, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task A_json_body_and_a_form_are_the_same_input_and_the_route_wins()
    {
        var json = await Post("/tool-route/north/widgets", JsonContent.Create(new { name = "a", count = 3, area = "south" }));
        var form = await Post("/tool-route/north/widgets", new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = "a", ["count"] = "3" }));

        json.Status.Should().Be(HttpStatusCode.Created, json.Body.ToString());
        form.Status.Should().Be(HttpStatusCode.Created, form.Body.ToString());
        json.Body.GetRawText().Should().Be(form.Body.GetRawText());
        json.Body.GetProperty("area").GetString().Should().Be("north", "a body never names the address");
        json.Body.GetProperty("count").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task A_value_that_cannot_bind_and_a_body_that_is_not_an_object_are_refused()
    {
        var bad = await Post("/tool-route/north/widgets", JsonContent.Create(new { name = "a", count = "many" }));
        bad.Status.Should().Be(HttpStatusCode.BadRequest);
        bad.Body.GetProperty("kind").GetString().Should().Be("invalid");
        bad.Body.GetProperty("errors").EnumerateObject().Select(field => field.Name).Should().ContainMatch("*ount");

        var list = await Post("/tool-route/north/widgets", JsonContent.Create(new[] { 1, 2 }));
        list.Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_verbs_gate_answers_the_route_with_no_mcp_module_present()
    {
        (await Post("/tool-route/restricted")).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("absent", HttpStatusCode.NotFound, "Nothing answers at this address.")]
    [InlineData("gated", HttpStatusCode.Unauthorized, "Not for you.")]
    [InlineData("conflict", HttpStatusCode.Conflict, "Already there.")]
    [InlineData("invalid", HttpStatusCode.BadRequest, "Not like that.")]
    public async Task One_mapping_answers_each_kind(string kind, HttpStatusCode status, string says)
    {
        var answer = await Post($"/tool-route/{kind}/refused");
        answer.Status.Should().Be(status);
        answer.Body.GetProperty("kind").GetString().Should().Be(kind);
        answer.Body.GetProperty("detail").GetString().Should().Be(says);
    }

    [Fact]
    public async Task A_command_that_never_ran_answers_503_and_success_stays_the_actions()
    {
        var never = await Post("/tool-route/never");
        never.Status.Should().Be(HttpStatusCode.ServiceUnavailable);
        never.Body.GetProperty("kind").GetString().Should().Be("failed");

        (await Post("/tool-route/done")).Status.Should().Be(HttpStatusCode.SeeOther);
    }

    [Fact]
    public void A_verb_declares_its_own_gate_or_takes_its_class_and_a_named_policy_cannot_compile()
    {
        var plain = typeof(ScopedOnItsClass.Inner).GetMethod(nameof(ScopedOnItsClass.Inner.Plain))!;
        var own = typeof(ScopedOnItsClass.Inner).GetMethod(nameof(ScopedOnItsClass.Inner.Own))!;
        AccessGateEvaluator.Describe(AccessGateCache.Compile(plain).For(EntityAuthorizeActions.Write)).Should().Contain("widgets:write");
        AccessGateEvaluator.Describe(AccessGateCache.Compile(own).For(EntityAuthorizeActions.Write)).Should().Contain("widgets:read");

        var named = typeof(NamedPolicy).GetMethod(nameof(NamedPolicy.Verb))!;
        Assert.Throws<AccessGateException>(() => AccessGateCache.Compile(named)).Message.Should().Contain("a-named-one");
    }
}

[Route("/tool-route")]
[Tool]
public sealed class ToolRouteController : ControllerBase
{
    [HttpPost("{area}/widgets")]
    public IActionResult Make(string area, ToolRouteSpec.WidgetInput input) => Created($"/tool-route/{area}/widgets/{input.Name}", new { area, input.Name, input.Count });

    [HttpPost("restricted")]
    [Access(all: "is:admin")]
    public IActionResult Restricted() => Ok();

    [HttpPost("{kind}/refused")]
    public IActionResult Refused(string kind) => this.Answer<string>(kind switch
    {
        "absent" => new Outcome<string>.Refused(Reason.Absent("A sentence that says too much.")),
        "gated" => new Outcome<string>.Refused(Reason.Gated("Not for you.")),
        "conflict" => new Outcome<string>.Refused(Reason.Conflict("Already there.")),
        _ => new Outcome<string>.Refused("Not like that."),
    }, _ => Ok());

    [HttpPost("never")]
    public async Task<IActionResult> Sent(CancellationToken ct)
        => this.Answer(await new ToolRouteSpec.Never("no").Send(ct), done => Ok(done));

    [HttpPost("done")]
    public async Task<IActionResult> Done() => await this.Answer(new Outcome<string>.Done("yes"), async done =>
    {
        await Task.Yield();
        return (IActionResult)new StatusCodeResult(StatusCodes.Status303SeeOther);
    });
}
