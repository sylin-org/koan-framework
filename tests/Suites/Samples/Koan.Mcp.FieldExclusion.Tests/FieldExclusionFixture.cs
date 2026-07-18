using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Mcp;
using Koan.Mcp.Hosting;
using Koan.Mcp.Options;
using Koan.Web.Controllers;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.FieldExclusion.Tests;

/// <summary>
/// Boots a real Koan + MCP + Web pipeline (AddKoan() reflective discovery, per ARCH-0079) hosting the
/// <see cref="CatalogItem"/> entity, and exposes helpers to drive MCP tools directly through the RPC handler.
/// </summary>
public class FieldExclusionFixture : TestHostFixtureBase
{
    public FieldExclusionFixture() : base(typeof(FieldExclusionFixture)) { }

    /// <summary>Public accessor for the booted service provider (base exposes it as protected).</summary>
    public IServiceProvider ServiceProvider => Services;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddKoan().AsProxiedApi();
        services.AddKoanWeb();

        // Drop the StdioTransport hosted service: it reads from stdin, which is dead in the test host,
        // and its background loop throws on shutdown ("test host process crashed" after all tests pass).
        // Mirrors TestPipelineFixture / StrictQuotaTestPipelineFixture in the McpCodeMode suite.
        var stdioService = services.FirstOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(StdioTransport));
        if (stdioService != null)
        {
            services.Remove(stdioService);
        }

        services.Configure<McpServerOptions>(o =>
        {
            o.Exposure = McpExposureMode.Full;
            o.EnableHttpSseTransport = true;
        });

        services.AddKoanControllersFrom<CatalogItemsController>();
    }

    protected override void ConfigureApp(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }

    /// <summary>Invokes an MCP tool through the real handler and returns the serialized CallToolResult.</summary>
    public async Task<JToken> CallToolAsync(string toolName, JObject? arguments, CancellationToken ct = default)
    {
        using var scope = Services.CreateScope();
        var server = scope.ServiceProvider.GetRequiredService<McpServer>();
        var handler = server.CreateHandler();
        var callParams = new McpRpcHandler.ToolsCallParams { Name = toolName, Arguments = arguments };
        var result = await handler.CallTool(callParams, ct);
        return JToken.Parse(JsonConvert.SerializeObject(result));
    }

    /// <summary>Returns the input schema (JObject) for a tool, read straight from the registry.</summary>
    public JObject GetToolInputSchema(string toolName)
    {
        var registry = Services.GetRequiredService<McpEntityRegistry>();
        foreach (var registration in registry.Registrations)
        {
            foreach (var tool in registration.Tools)
            {
                if (string.Equals(tool.Name, toolName, StringComparison.OrdinalIgnoreCase))
                {
                    return tool.InputSchema;
                }
            }
        }

        throw new InvalidOperationException($"Tool '{toolName}' was not discovered.");
    }
}
