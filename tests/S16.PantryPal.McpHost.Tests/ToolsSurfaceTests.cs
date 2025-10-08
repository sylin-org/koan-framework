using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace S16.PantryPal.McpHost.Tests;

public class ToolsSurfaceTests : IClassFixture<DockerMcpHostFixture>
{
    private readonly DockerMcpHostFixture _fx;
    public ToolsSurfaceTests(DockerMcpHostFixture fx) => _fx = fx;

    private record JsonRpcRequest(string Jsonrpc, string Id, string Method, object? Params);

    [Fact]
    public async Task ToolsList_Includes_CodeExecute()
    {
        if (Environment.GetEnvironmentVariable("S16_MCPHOST_DOCKER_UNAVAILABLE") == "1")
            return;
        var req = new JsonRpcRequest("2.0", Guid.NewGuid().ToString(), "tools/list", null);
        var response = await _fx.Client.PostAsJsonAsync("/mcp/rpc", req);
        response.EnsureSuccessStatusCode();
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tools = payload.RootElement.GetProperty("result").GetProperty("tools");
        tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).Should().Contain("koan.code.execute");
    }
}
