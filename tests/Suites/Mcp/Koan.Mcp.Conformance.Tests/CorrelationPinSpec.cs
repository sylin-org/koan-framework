using System.Security.Claims;
using System.Threading.Tasks;
using Koan.Mcp.Execution;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// AN9 (docs/assessment/09 §11.3) — the "pin": a per-conversation correlation id for audit stitching,
/// carrying ZERO authority. It is client-owned (accepted opaque + untrusted), minted server-side when
/// absent, and echoed back so an agent can stitch its trajectory. THE INVARIANT — continuity ≠ authority:
/// the pin is never consulted for permission (the access authority is principal-only).
/// </summary>
public sealed class CorrelationPinSpec : IClassFixture<ConformanceFixture>
{
    private readonly ConformanceFixture _fx;

    public CorrelationPinSpec(ConformanceFixture fx) => _fx = fx;

    private async Task<string?> CorrelationOf(JObject? arguments)
    {
        var tool = _fx.ResolveToolName("gadget", EntityEndpointOperationKind.Collection);
        var result = await _fx.CallToolAsync(tool, arguments);
        return result["meta"]?["diagnostics"]?["correlationId"]?.Value<string>();
    }

    [Fact]
    public async Task A_client_supplied_pin_is_accepted_and_echoed()
    {
        var corr = await CorrelationOf(new JObject { ["correlationId"] = "client-pin-123" });
        corr.Should().Be("client-pin-123", "a client-owned correlation id is accepted opaque and echoed for stitching");
    }

    [Fact]
    public async Task A_pin_is_minted_when_the_client_supplies_none()
    {
        var corr = await CorrelationOf(new JObject());
        corr.Should().NotBeNullOrEmpty("the server mints a correlation id when none is supplied");
        corr!.Length.Should().Be(32, "a minted pin is a GUIDv7 'n' string (StringId.New)");
    }

    [Fact]
    public void Continuity_is_not_authority()
    {
        // The pin is NOT an input to the access decision — McpToolAccessPolicy takes only the principal.
        // An anonymous caller is denied a scoped capability; supplying any correlation id / pin changes
        // nothing (accepting a pin in place of a grant would be session fixation).
        McpToolAccessPolicy.IsPermitted(new ClaimsPrincipal(new ClaimsIdentity()), requiresAuth: false, new[] { "vault:read" })
            .Should().BeFalse("authority is per-request against the principal, never the pin");
    }
}
