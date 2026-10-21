using System.Threading.Tasks;
using Koan.Mcp.Options;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>Boots an MCP host exposing a role-gated, a scope-gated and two <c>[Door]</c> entities.</summary>
public sealed class RefusalFixture : McpHarnessFixtureBase
{
    protected override void ConfigureMcp(McpServerOptions options)
    {
        options.AllowedEntities.Add("rolevault");
        options.AllowedEntities.Add("coffer");
        options.AllowedEntities.Add("showcase");
        options.AllowedEntities.Add("vaultroom");
    }
}

/// <summary>
/// SEC-0010: the shared endpoint's refusal is a failed tool call. A denial renders as an error with a stable code
/// (never an empty success, never the gate's description); a short circuit with an error status renders as the
/// same refusal REST returns.
/// </summary>
public sealed class RefusalRenderingSpec : IClassFixture<RefusalFixture>
{
    private readonly RefusalFixture _fx;

    public RefusalRenderingSpec(RefusalFixture fx) => _fx = fx;

    private string Tool(string entity, EntityEndpointOperationKind operation) => _fx.ResolveToolName(entity, operation);

    private static string? ErrorCode(JToken call) => call["meta"]?["errorCode"]?.Value<string>();

    [Fact]
    public async Task A_forbidden_collection_is_an_error_and_does_not_name_the_role()
    {
        var call = await _fx.CallToolAsAsync(Tool("rolevault", EntityEndpointOperationKind.Collection), new JObject(),
            McpHarnessFixtureBase.Principal(scopes: "anything", roles: "user"));

        McpHarnessFixtureBase.IsError(call).Should().BeTrue(call.ToString());
        ErrorCode(call).Should().Be("forbidden");
        McpHarnessFixtureBase.ContentText(call).Should().NotBe("[]", "a denied read is not an empty collection");
        call["meta"]!["diagnostics"]!["shortCircuitStatusCode"]!.Value<int>().Should().Be(403);
        call.ToString().Should().NotContain("admin").And.NotContain("role in", "a role gate is a privilege tier and stays silent");
    }

    [Fact]
    public async Task An_anonymous_caller_on_a_gated_entity_is_unauthenticated()
    {
        var call = await _fx.CallToolAsAsync(Tool("coffer", EntityEndpointOperationKind.Collection), new JObject(),
            new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()));

        McpHarnessFixtureBase.IsError(call).Should().BeTrue(call.ToString());
        ErrorCode(call).Should().Be("unauthenticated");
        call["meta"]!["diagnostics"]!["shortCircuitStatusCode"]!.Value<int>().Should().Be(401);
        McpHarnessFixtureBase.IsShortCircuited(call).Should().BeTrue("the rendered refusal stays readable in meta.shortCircuit");
    }

    [Fact]
    public async Task A_door_entity_discloses_what_its_scope_gate_needs()
    {
        var call = await _fx.CallToolAsAsync(Tool("showcase", EntityEndpointOperationKind.Upsert),
            new JObject { ["model"] = new JObject { ["title"] = "x" } }, McpHarnessFixtureBase.Principal(scopes: "other"));

        McpHarnessFixtureBase.IsError(call).Should().BeTrue(call.ToString());
        ErrorCode(call).Should().Be("forbidden");
        McpHarnessFixtureBase.ContentText(call).Should().Contain("requires scope:showcase:write");
        call["meta"]!["shortCircuit"]!["needs"]!.Value<string>().Should().Be("requires scope:showcase:write");
    }

    [Fact]
    public async Task A_door_entity_keeps_a_role_gate_silent()
    {
        var call = await _fx.CallToolAsAsync(Tool("vaultroom", EntityEndpointOperationKind.Delete),
            new JObject { ["id"] = "missing" }, McpHarnessFixtureBase.Principal(scopes: "other", roles: "user"));

        McpHarnessFixtureBase.IsError(call).Should().BeTrue(call.ToString());
        ErrorCode(call).Should().Be("forbidden");
        call.ToString().Should().NotContain("admin");
    }

    [Fact]
    public async Task A_missing_row_is_a_not_found_error()
    {
        var call = await _fx.CallToolAsync(Tool("showcase", EntityEndpointOperationKind.GetById), new JObject { ["id"] = "no-such-row" });

        McpHarnessFixtureBase.IsError(call).Should().BeTrue(call.ToString());
        ErrorCode(call).Should().Be("not_found");
        call["meta"]!["diagnostics"]!["shortCircuitStatusCode"]!.Value<int>().Should().Be(404);
    }

    [Fact]
    public async Task A_bad_request_is_an_error_carrying_the_reason()
    {
        // Request text is refused with a 400 on every surface (WEB-0074); MCP now reports it as the error it is.
        var call = await _fx.CallToolAsync(Tool("showcase", EntityEndpointOperationKind.Collection), new JObject { ["q"] = "1=1" });

        McpHarnessFixtureBase.IsError(call).Should().BeTrue(call.ToString());
        ErrorCode(call).Should().Be("rejected");
        call["meta"]!["diagnostics"]!["shortCircuitStatusCode"]!.Value<int>().Should().Be(400);
        McpHarnessFixtureBase.ContentText(call).Should().Contain("q is not accepted on this endpoint");
    }
}
