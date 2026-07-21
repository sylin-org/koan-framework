using Newtonsoft.Json.Linq;

namespace Koan.Samples.McpCodeMode.Tests;

/// <summary>
/// AI-0014 — the RPC invoke path (tools/call by name) must honor the same code-mode gates as
/// tools/list and the capabilities/SDK endpoints. Two independent gates, one error contract
/// (<c>code_mode_disabled</c>):
///   1. <c>CodeMode:Enabled=false</c> kill switch (covered by <see cref="CodeModeDisabledFixture"/>).
///   2. The active exposure mode does not surface code (covered by <see cref="ToolsOnlyExposureFixture"/>).
/// The headline regression: hiding a tool from tools/list is not a security control — a client can
/// still call koan.code.execute by name and run sandboxed JavaScript unless the invoke path refuses it.
/// </summary>
public class CodeModeGatingSpec :
    IClassFixture<CodeModeDisabledFixture>,
    IClassFixture<ToolsOnlyExposureFixture>
{
    private readonly CodeModeDisabledFixture _disabled;
    private readonly ToolsOnlyExposureFixture _toolsOnly;

    public CodeModeGatingSpec(CodeModeDisabledFixture disabled, ToolsOnlyExposureFixture toolsOnly)
    {
        _disabled = disabled;
        _toolsOnly = toolsOnly;
    }

    // ---- Gate 1: CodeMode:Enabled=false (Exposure=Full) ----

    [Fact(DisplayName = "Disabled kill switch hides code tools but keeps entity tools")]
    public async Task Disabled_hides_code_tools_keeps_entity_tools()
    {
        var names = await ListToolNames(_disabled);
        names.Should().NotContain("koan.code.execute");
        names.Should().NotContain("koan.code.validate");
        names.Any(n => n is not null && !n.StartsWith("koan.code.")).Should()
            .BeTrue("entity tools must still be exposed under Full when only code mode is disabled");
    }

    [Fact(DisplayName = "Disabled kill switch refuses koan.code.execute called by name")]
    public async Task Disabled_refuses_execute_by_name()
    {
        var resp = await Call(_disabled, "koan.code.execute", new { code = "SDK.Out.answer('should never run');" });
        AssertCodeModeDisabled(resp);
    }

    [Fact(DisplayName = "Disabled kill switch refuses koan.code.validate called by name")]
    public async Task Disabled_refuses_validate_by_name()
    {
        var resp = await Call(_disabled, "koan.code.validate", new { code = "function run() { return 1; }" });
        AssertCodeModeDisabled(resp);
    }

    // ---- Gate 2: Exposure=Tools (code mode otherwise enabled) ----

    [Fact(DisplayName = "Tools exposure hides code tools from tools/list")]
    public async Task ToolsExposure_hides_code_tools()
    {
        var names = await ListToolNames(_toolsOnly);
        names.Should().NotContain("koan.code.execute");
        names.Should().NotContain("koan.code.validate");
    }

    [Fact(DisplayName = "Tools exposure refuses koan.code.execute called by name (the reported bypass)")]
    public async Task ToolsExposure_refuses_execute_by_name()
    {
        var resp = await Call(_toolsOnly, "koan.code.execute", new { code = "SDK.Out.answer('should never run');" });
        AssertCodeModeDisabled(resp);
    }

    [Fact(DisplayName = "Tools exposure refuses koan.code.validate called by name")]
    public async Task ToolsExposure_refuses_validate_by_name()
    {
        var resp = await Call(_toolsOnly, "koan.code.validate", new { code = "function run() { return 1; }" });
        AssertCodeModeDisabled(resp);
    }

    private static void AssertCodeModeDisabled(JToken resp)
    {
        resp["isError"]?.Value<bool>().Should().BeTrue("a gated code call must return an error envelope");
        resp["meta"]?["errorCode"]?.Value<string>().Should().Be("code_mode_disabled");
    }

    private static async Task<List<string?>> ListToolNames(TestHostFixtureBase fx)
    {
        var listObj = await fx.InvokeRpc("tools/list", Guid.NewGuid().ToString("n"));
        var root = JToken.FromObject(listObj!);
        var tools = root["tools"] as JArray ?? (root["Tools"] as JArray) ?? new JArray();
        return tools.Select(t => t["name"]?.Value<string>()).ToList();
    }

    private static async Task<JToken> Call(TestHostFixtureBase fx, string name, object args)
    {
        var argObj = JObject.FromObject(args);
        var resultObj = await fx.InvokeRpc("tools/call", Guid.NewGuid().ToString("n"), name, argObj);
        return JToken.FromObject(resultObj!);
    }
}
