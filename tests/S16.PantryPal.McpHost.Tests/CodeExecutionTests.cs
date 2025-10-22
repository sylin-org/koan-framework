using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace S16.PantryPal.McpHost.Tests;

public class CodeExecutionTests : IClassFixture<DockerMcpHostFixture>
{
    private readonly DockerMcpHostFixture _fx;
    public CodeExecutionTests(DockerMcpHostFixture fx) => _fx = fx;

    private record Rpc(string Jsonrpc, string Id, string Method, object Params);

    private object ExecPayload(string code) => new Rpc("2.0", Guid.NewGuid().ToString(), "tools/call", new
    {
        name = "koan.code.execute",
        arguments = new { code }
    });

    [Fact]
    public async Task CodeExecute_ReturnsRecipeArray()
    {
        if (Environment.GetEnvironmentVariable("S16_MCPHOST_DOCKER_UNAVAILABLE") == "1")
            return;
        // Simple script: fetch a small collection and answer with count
        var script = "const r = SDK.Entities.Recipe.collection({ pageSize: 2 }); SDK.Out.answer(JSON.stringify({count:r.items.length}));";
        var resp = await _fx.Client.PostAsJsonAsync("/mcp/rpc", ExecPayload(script));
        resp.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result").GetProperty("content");
        var text = result[0].GetProperty("text").GetString();
        text.Should().Contain("count");
    }
}
