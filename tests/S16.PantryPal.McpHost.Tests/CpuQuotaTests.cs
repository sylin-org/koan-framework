using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace S16.PantryPal.McpHost.Tests;

public class CpuQuotaTests : IClassFixture<DockerMcpHostFixture>
{
    private readonly DockerMcpHostFixture _fx;
    public CpuQuotaTests(DockerMcpHostFixture fx) => _fx = fx;

    private record Rpc(string Jsonrpc, string Id, string Method, object Params);

    private object ExecPayload(string code) => new Rpc("2.0", Guid.NewGuid().ToString(), "tools/call", new
    {
        name = "koan.code.execute",
        arguments = new { code }
    });

    [Fact(Skip="Quota limits not yet forcibly configurable in test context; enable once options injection added.")]
    public async Task BusyLoop_TriggersCpuQuotaError()
    {
        if (Environment.GetEnvironmentVariable("S16_MCPHOST_DOCKER_UNAVAILABLE") == "1")
            return;
        var script = "let x=0; for (let i=0;i<1e9;i++){ x+=i; } SDK.Out.answer(x.toString());";
        var resp = await _fx.Client.PostAsJsonAsync("/mcp/rpc", ExecPayload(script));
        resp.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
    }
}
