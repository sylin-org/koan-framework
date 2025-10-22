using Newtonsoft.Json.Linq;

namespace Koan.Samples.McpCodeMode.Tests;

public class ExposureModeSpec : IClassFixture<TestPipelineFixture>
{
    private readonly TestPipelineFixture _fx;
    public ExposureModeSpec(TestPipelineFixture fx) => _fx = fx;

    [Fact(DisplayName = "Exposure Auto should include code + entity tools (temporary Full fallback)")]
    public async Task AutoExposure_ShouldListCodeAndEntityTools()
    {
        // Arrange: force server options Exposure=null so ResolveExposureMode uses default Auto path
        // We can't reconfigure existing fixture services post-build easily, so we directly invoke list
        // expecting fallback logic to treat Auto as Full (see TODO in McpRpcHandler).
        var listObj = await _fx.InvokeRpcAsync("tools/list", Guid.NewGuid().ToString("n"));
        listObj.Should().NotBeNull();
        var root = JToken.FromObject(listObj!);
        var tools = root["tools"] as JArray ?? root as JArray; // fixture may return ToolsListResponse or direct array
        if (tools == null)
        {
            // When InvokeRpcAsync returns ToolsListResponse directly
            tools = (root["Tools"] as JArray) ?? new JArray();
        }
        // Extract names
        var names = tools.Select(t => t["name"]?.Value<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        names.Should().Contain("koan.code.execute", "code execution tool must be present in Auto mode fallback");
        // At least one entity tool (heuristic: any tool not equal to code execution)
        names.Any(n => n != null && n != "koan.code.execute").Should().BeTrue("Expected at least one entity tool in Auto mode fallback");
    }
}
