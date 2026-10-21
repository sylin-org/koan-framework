using System.Threading.Tasks;
using Koan.Mcp.Options;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>Boots an open entity and the conformance custom verbs under <c>Exposure = Code</c>.</summary>
public sealed class CodeExposureFixture : McpHarnessFixtureBase
{
    protected override McpExposureMode Exposure => McpExposureMode.Code;

    protected override void ConfigureMcp(McpServerOptions options) => options.AllowedEntities.Add("marble");
}

/// <summary>
/// SEC-0014: under <c>Exposure = Code</c> entity tools and custom verbs are neither listed nor callable by name; the
/// sandbox's SDK calls still run.
/// </summary>
public sealed class ExposureEnforcementSpec : IClassFixture<CodeExposureFixture>
{
    private readonly CodeExposureFixture _fx;

    public ExposureEnforcementSpec(CodeExposureFixture fx) => _fx = fx;

    [Fact]
    public async Task An_entity_tool_is_refused_by_name()
    {
        var call = await _fx.CallToolAsync(_fx.ResolveToolName("marble", EntityEndpointOperationKind.DeleteAll), new JObject());

        McpHarnessFixtureBase.IsError(call).Should().BeTrue(call.ToString());
        call["meta"]!["errorCode"]!.Value<string>().Should().Be("exposure_mode");
    }

    [Fact]
    public async Task A_custom_verb_is_refused_by_name()
    {
        var call = await _fx.CallToolAsync("gadget_ping", new JObject());

        McpHarnessFixtureBase.IsError(call).Should().BeTrue(call.ToString());
        call["meta"]!["errorCode"]!.Value<string>().Should().Be("exposure_mode");
    }

    [Fact]
    public async Task The_sandbox_still_reaches_entities()
    {
        var call = await _fx.CallToolAsync("koan.code.execute",
            new JObject { ["code"] = "SDK.Out.answer('' + SDK.Entities.marble.collection().count);" });

        McpHarnessFixtureBase.IsError(call).Should().BeFalse(call.ToString());
        McpHarnessFixtureBase.ContentText(call).Should().MatchRegex("^[0-9]+$");
    }
}
