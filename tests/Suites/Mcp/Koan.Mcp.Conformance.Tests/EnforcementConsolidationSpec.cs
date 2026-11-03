using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Koan.Mcp;
using Koan.Mcp.TestKit;
using Koan.Mcp.Execution;
using Koan.Web.Authorization;
using Koan.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// SEC-0004, ARCH-0141 — MCP enforcement has one authority, with NO per-transport duplication: the gate. An ENTITY
/// tool asks it for the entity's type, a custom verb for its method, and STDIO asks as its one principal, anonymous and
/// <c>origin:local</c> (SEC-0017). A value that cannot bind to a custom verb is refused, naming it.
/// </summary>
public sealed class EnforcementConsolidationSpec : IClassFixture<ConformanceFixture>
{
    private readonly ConformanceFixture _fx;

    public EnforcementConsolidationSpec(ConformanceFixture fx) => _fx = fx;

    // ---- Custom-verb authority: the one gate ------------------------------------------------------

    [Fact]
    public async Task A_custom_verb_answers_to_the_gate_on_a_call_and_in_a_list()
    {
        var allowed = await _fx.CallToolAsAsync("gadget_admin", new JObject(), User("gadget:admin"));
        McpHarnessFixtureBase.IsError(allowed).Should().BeFalse(allowed.ToString());

        foreach (var caller in new[] { User("other"), Anon() })
        {
            var denied = await _fx.CallToolAsAsync("gadget_admin", new JObject(), caller);
            McpHarnessFixtureBase.IsError(denied).Should().BeTrue(denied.ToString());
            denied["_meta"]!["errorCode"]!.Value<string>().Should().Be("gated");
        }

        (await _fx.CallToolAsync("gadget_admin", new JObject()))["isError"]!.Value<bool>().Should().BeTrue("STDIO asks as anyone does");
        (await _fx.ListToolsAsync()).OfType<JObject>().Select(t => t["name"]?.Value<string>()).Should().NotContain("gadget_admin")
            .And.Contain("gadget_ping");
    }

    [Fact]
    public async Task A_value_that_cannot_bind_to_a_custom_verb_is_refused_with_its_name()
    {
        var unreadable = await _fx.CallToolAsync("gadget_count", new JObject { ["count"] = "many" });
        McpHarnessFixtureBase.IsError(unreadable).Should().BeTrue(unreadable.ToString());
        McpHarnessFixtureBase.ContentText(unreadable).Should().StartWith("count:");
        unreadable["_meta"]!["errorCode"]!.Value<string>().Should().Be("invalid");

        var missing = await _fx.CallToolAsync("gadget_count", new JObject { ["step"] = 2 });
        McpHarnessFixtureBase.IsError(missing).Should().BeTrue(missing.ToString());
        McpHarnessFixtureBase.ContentText(missing).Should().StartWith("count:");

        var good = await _fx.CallToolAsync("gadget_count", new JObject { ["count"] = 3 });
        McpHarnessFixtureBase.ContentText(good).Should().Be("4", "an optional argument left out takes its default");
    }

    [Fact]
    public async Task A_refusal_a_custom_verb_throws_keeps_its_kind_and_absence_says_one_thing()
    {
        var absent = await _fx.CallToolAsync("gadget_missing", new JObject());
        McpHarnessFixtureBase.IsError(absent).Should().BeTrue(absent.ToString());
        absent["_meta"]!["errorCode"]!.Value<string>().Should().Be("absent");
        McpHarnessFixtureBase.ContentText(absent).Should().Be(Koan.Web.Controllers.Answers.Nothing);
    }

    // ---- Entity-tool authority: the data-layer [Access] gate ------------------------------------

    [Fact]
    public void The_entity_gate_denies_an_anonymous_caller_a_scoped_tool()
    {
        var registry = _fx.Services.GetRequiredService<McpEntityRegistry>();
        var gateCache = _fx.Services.GetRequiredService<IAccessGateCache>();
        registry.TryGetRegistration("vault", out var registration).Should().BeTrue();
        var tool = registration.Tools.First();

        McpEntityGate.CoarseAllows(gateCache, registration.EntityType, tool.Operation, Anon()).Should().BeFalse(
            "the data-layer gate denies an unscoped caller the scoped entity tool — the entity-tool authority");
    }

    private static ClaimsPrincipal Anon() => new(new ClaimsIdentity());

    private static ClaimsPrincipal User(params string[] scopes)
    {
        var claims = scopes.Length > 0 ? new[] { new Claim("scope", string.Join(' ', scopes)) } : Array.Empty<Claim>();
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }
}
