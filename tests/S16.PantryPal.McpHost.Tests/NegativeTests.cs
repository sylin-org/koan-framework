using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace S16.PantryPal.McpHost.Tests;

public class NegativeTests : IClassFixture<DockerMcpHostFixture>
{
    private readonly DockerMcpHostFixture _fx;
    public NegativeTests(DockerMcpHostFixture fx) => _fx = fx;

    private record Rpc(string Jsonrpc, string Id, string Method, object Params);
    private object ExecPayload(string code) => new Rpc("2.0", Guid.NewGuid().ToString(), "tools/call", new
    {
        name = "koan.code.execute",
        arguments = new { code }
    });

    [Fact]
    public async Task UnknownEntity_ProducesError()
    {
        if (Environment.GetEnvironmentVariable("S16_MCPHOST_DOCKER_UNAVAILABLE") == "1")
            return;
        var script = "SDK.Entities.DoesNotExist.collection(); SDK.Out.answer('x');";
        var resp = await _fx.Client.PostAsJsonAsync("/mcp/rpc", ExecPayload(script));
        resp.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("error", out var error).Should().BeTrue("error object expected");
        error.GetProperty("message").GetString().Should().Contain("DoesNotExist");
    }
}
