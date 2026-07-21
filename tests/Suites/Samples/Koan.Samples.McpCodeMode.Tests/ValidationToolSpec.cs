using Newtonsoft.Json.Linq;

namespace Koan.Samples.McpCodeMode.Tests;

public class ValidationToolSpec : IClassFixture<TestPipelineFixture>
{
    private readonly TestPipelineFixture _fx;
    public ValidationToolSpec(TestPipelineFixture fx) => _fx = fx;

    private async Task<JArray> ListTools()
    {
        var listObj = await _fx.InvokeRpc("tools/list", Guid.NewGuid().ToString("n"));
        var root = JToken.FromObject(listObj!);
        var tools = root["tools"] as JArray ?? root as JArray;
        if (tools == null)
        {
            tools = (root["Tools"] as JArray) ?? new JArray();
        }
        return tools;
    }

    private async Task<JToken> Call(string name, object args)
    {
        var argObj = JObject.FromObject(args);
        var resultObj = await _fx.InvokeRpc("tools/call", Guid.NewGuid().ToString("n"), name, argObj);
        return JToken.FromObject(resultObj!);
    }

    [Fact(DisplayName = "Validation tool should appear with code execution tool in Full/Auto fallback mode")] 
    public async Task List_ShouldContainValidationTool()
    {
        var tools = await ListTools();
        var names = tools.Select(t => t["name"]?.Value<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        names.Should().Contain("koan.code.validate");
        names.Should().Contain("koan.code.execute");
    }

    [Fact(DisplayName = "Validation tool returns valid=true for simple script")]
    public async Task Validate_ValidScript()
    {
        var resp = await Call("koan.code.validate", new { code = "function run() { return 1+1; }" });
        var text = resp["content"]?[0]?["text"]?.Value<string>();
        var obj = JObject.Parse(text!);
        obj["valid"]!.Value<bool>().Should().BeTrue();
    }

    [Fact(DisplayName = "Validation tool returns valid=false with error for syntax issue")]
    public async Task Validate_InvalidScript()
    {
        var resp = await Call("koan.code.validate", new { code = "function run( {" });
        var text = resp["content"]?[0]?["text"]?.Value<string>();
        var obj = JObject.Parse(text!);
        obj["valid"]!.Value<bool>().Should().BeFalse();
        obj["error"].Should().NotBeNull();
    }

    [Fact(DisplayName = "Validation tool returns valid=false for empty code")]
    public async Task Validate_Empty()
    {
        var resp = await Call("koan.code.validate", new { code = "" });
        var text = resp["content"]?[0]?["text"]?.Value<string>();
        var obj = JObject.Parse(text!);
        obj["valid"]!.Value<bool>().Should().BeFalse();
    }
}
