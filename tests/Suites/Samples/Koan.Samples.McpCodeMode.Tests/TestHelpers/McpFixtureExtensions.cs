using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Koan.Samples.McpCodeMode.Tests;

internal static class McpFixtureExtensions
{
    public static async Task<object?> InvokeRpcAsync(this Koan.Testing.KoanTestPipelineFixtureBase fixture, string method, string id, string? toolName = null, JObject? arguments = null, CancellationToken ct = default)
    {
        // Access protected Services via reflection since extension method lacks subclass access
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
    var baseType = fixture.GetType().BaseType; // should be KoanTestPipelineFixtureBase subclass
    if (baseType == null) throw new InvalidOperationException("Fixture base type not found");
    var servicesProp = baseType.GetProperty("Services", flags | System.Reflection.BindingFlags.FlattenHierarchy);
    var provider = (IServiceProvider?)servicesProp?.GetValue(fixture) ?? throw new InvalidOperationException("Unable to access fixture service provider");
        using var scope = provider.CreateScope();
        var server = scope.ServiceProvider.GetRequiredService<Koan.Mcp.Hosting.McpServer>();
        var handler = server.CreateHandler();
        if (method == "tools/list")
        {
            return await handler.ListToolsAsync(ct);
        }
        if (method == "tools/call")
        {
            if (string.IsNullOrWhiteSpace(toolName)) throw new ArgumentException("toolName required for tools/call");
            var callParams = new Koan.Mcp.Hosting.McpRpcHandler.ToolsCallParams { Name = toolName, Arguments = arguments };
            var callResult = await handler.CallToolAsync(callParams, ct);
            if (callResult.Success && callResult.Result is not null)
            {
                return callResult.Result; // flattened success payload
            }
            return callResult; // full envelope on failure
        }
        throw new NotSupportedException($"Unsupported method: {method}");
    }
}