using Newtonsoft.Json.Linq;

namespace Koan.Samples.McpCodeMode.Tests;

public class QuotaStrictSpec : IClassFixture<StrictQuotaTestPipelineFixture>
{
    private readonly StrictQuotaTestPipelineFixture _fx;
    public QuotaStrictSpec(StrictQuotaTestPipelineFixture fx) => _fx = fx;

    private async Task<JToken> CallAsync(string name, object args)
    {
        var json = JObject.FromObject(args);
        var resultObj = await _fx.InvokeRpcAsync("tools/call", Guid.NewGuid().ToString("n"), name, json);
        return JToken.FromObject(resultObj!);
    }

    [Fact(DisplayName = "Exceeding MaxSdkCalls=2 deterministically returns sdk_calls_exceeded")]
    public async Task Quota_ShouldEnforceDeterministically()
    {
        var code = "function run() { SDK.Entities.Todo.upsert({ title: 'a'}); SDK.Entities.Todo.upsert({ title: 'b'}); SDK.Entities.Todo.upsert({ title: 'c'}); SDK.Out.answer('done'); }";
    var result = await CallAsync("koan.code.execute", new { code, correlationId = "strict-maxsdk" });
    result["ErrorCode"]!.Value<string>().Should().Be("sdk_calls_exceeded");
    }

    [Fact(DisplayName = "RequireAnswer=true returns missing_answer when no answer produced")]
    public async Task RequireAnswer_ShouldAlwaysEnforce()
    {
        var code = "function run() { SDK.Entities.Todo.upsert({ title: 'x'}); }"; // no answer
    var result = await CallAsync("koan.code.execute", new { code, correlationId = "strict-miss-answer" });
    result["ErrorCode"]!.Value<string>().Should().Be("missing_answer");
    }

    [Fact(DisplayName = "Valid script within quotas succeeds and returns diagnostics")]
    public async Task Success_ShouldReturnDiagnostics()
    {
        var code = "function run() { SDK.Entities.Todo.upsert({ title: 'ok'}); SDK.Out.answer('yes'); }"; // 1 call <= quota
    var result = await CallAsync("koan.code.execute", new { code, correlationId = "strict-success" });
    result["text"]!.Value<string>().Should().Be("yes");
    }
}
