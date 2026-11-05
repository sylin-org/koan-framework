using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
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

    public sealed class ScopedOnItsClass
    {
        [Access(all: "has:scope:widgets:write")]
        public sealed class Inner
        {
            public void Plain() { }

            [RequireScope("widgets:read")]
            public void Own() { }
        }

        public class Base
        {
            public void Inherited() { }
        }

        [Access(all: "has:scope:widgets:write")]
        public sealed class Derived : Base;
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
    [InlineData("absent", HttpStatusCode.NotFound, "absent", "Nothing answers at this address.", "absent")]
    [InlineData("gated", HttpStatusCode.Unauthorized, "gated", "Not for you.", "timed-out")]
    [InlineData("conflict", HttpStatusCode.Conflict, "conflict", "Already there.", "conflict")]
    [InlineData("taken", HttpStatusCode.Conflict, "conflict", "Another one has this id.", "id-taken")]
    [InlineData("invalid", HttpStatusCode.BadRequest, "invalid", "Not like that.", "invalid")]
    public async Task One_mapping_answers_each_kind_with_its_word(string refusal, HttpStatusCode status, string kind, string says, string word)
    {
        var answer = await Post($"/tool-route/{refusal}/refused");
        answer.Status.Should().Be(status);
        answer.Body.GetProperty("kind").GetString().Should().Be(kind);
        answer.Body.GetProperty("detail").GetString().Should().Be(says);
        answer.Body.GetProperty("word").GetString().Should().Be(word, "every refusal carries a word, its kind's where it names none");
        answer.Body.TryGetProperty("gate", out _).Should().BeFalse("the word has one name on the wire");
    }

    [Fact]
    public async Task An_invalid_reason_that_names_its_field_is_refused_on_that_field()
    {
        var answer = await Post("/tool-route/field/refused");
        answer.Status.Should().Be(HttpStatusCode.BadRequest);
        answer.Body.GetProperty("kind").GetString().Should().Be("invalid");
        answer.Body.GetProperty("errors").GetProperty("count").EnumerateArray().Select(said => said.GetString()).Should().Equal("Not that many.");
        answer.Body.GetProperty("detail").GetString().Should().Be("Not that many.");
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
    public void A_verb_and_its_class_both_hold_and_a_derived_class_reaches_what_it_inherits()
    {
        var own = AccessGateEvaluator.Describe(AccessGateCache.Compile(typeof(ScopedOnItsClass.Inner).GetMethod(nameof(ScopedOnItsClass.Inner.Own))!).For(EntityAuthorizeActions.Write));
        own.Should().Contain("widgets:write").And.Contain("widgets:read");

        var inherited = AccessGateEvaluator.Describe(AccessGateCache.Compile(typeof(ScopedOnItsClass.Derived).GetMethod(nameof(ScopedOnItsClass.Base.Inherited))!).For(EntityAuthorizeActions.Write));
        inherited.Should().Contain("widgets:write");

        AccessGateCache.Compile(typeof(ScopedOnItsClass.Base).GetMethod(nameof(ScopedOnItsClass.Base.Inherited))!).For(EntityAuthorizeActions.Write).IsOpen.Should().BeTrue();
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
        "gated" => new Outcome<string>.Refused(Reason.Gated("timed-out", "Not for you.")),
        "conflict" => new Outcome<string>.Refused(Reason.Conflict("Already there.")),
        "taken" => new Outcome<string>.Refused(Reason.Conflict("id-taken", "Another one has this id.")),
        "field" => new Outcome<string>.Refused(Reason.Invalid("count", "Not that many.")),
        _ => new Outcome<string>.Refused(Reason.Invalid("Not like that.")),
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
