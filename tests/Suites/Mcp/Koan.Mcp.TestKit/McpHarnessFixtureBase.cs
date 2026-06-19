using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Mcp;
using Koan.Mcp.Extensions;
using Koan.Mcp.Hosting;
using Koan.Mcp.Options;
using Koan.Web.Endpoints;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.TestKit;

/// <summary>
/// Reusable MCP capability harness. Boots a real Koan + MCP + Web pipeline through <c>AddKoan()</c>
/// reflective discovery (ARCH-0079) on an in-memory ASP.NET Core TestServer, and exposes the surfaces
/// an agent-native capability test needs: drive MCP tools through the real RPC handler
/// (<see cref="CallToolAsync"/>), read a tool's input schema (<see cref="GetToolInputSchema"/>), resolve
/// a tool's name from the registry by entity + operation (<see cref="ResolveToolName"/>), and a plain
/// REST client for out-of-band seeding (<see cref="CreateClient"/>).
///
/// Subclasses register the entities/controllers/hooks under test via <see cref="ConfigureServices"/>
/// and tune the MCP server via <see cref="ConfigureMcp"/>. The whole agent-native (AN) program reuses
/// this so every MCP capability is verified end-to-end, not through a mock.
/// </summary>
public abstract class McpHarnessFixtureBase : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;

    /// <summary>The booted service provider (real <c>AddKoan()</c> graph).</summary>
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("MCP harness host not started.");

    /// <summary>ASP.NET host environment. Non-production so the relational DDL guard allows AutoCreate.</summary>
    protected virtual string Environment => "Development";

    /// <summary>Default exposure mode for the MCP server. Full generates both Tools-mode and Code-Mode surfaces.</summary>
    protected virtual McpExposureMode Exposure => McpExposureMode.Full;

    /// <summary>Register entities/controllers/hooks and any extra services under test.</summary>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>Tune the MCP server options (allow/deny lists, exposure overrides, audit, …).</summary>
    protected virtual void ConfigureMcp(McpServerOptions options) { }

    public async ValueTask InitializeAsync()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment(Environment);
                web.ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddKoan().AsProxiedApi();
                    services.AddKoanMcp();
                    services.AddKoanWeb();

                    // Drop the StdioTransport hosted service: it reads from stdin (dead in the test host)
                    // and its background loop throws on shutdown. Mirrors the McpCodeMode/FieldExclusion fixtures.
                    var stdio = services.FirstOrDefault(d =>
                        d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(StdioTransport));
                    if (stdio is not null) services.Remove(stdio);

                    services.Configure<McpServerOptions>(o =>
                    {
                        o.Exposure = Exposure;
                        o.EnableHttpSseTransport = true;
                        ConfigureMcp(o);
                    });

                    ConfigureServices(services);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapKoanMcpEndpoints();
                        endpoints.MapControllers();
                    });
                });
            });

        _host = await builder.StartAsync(TestContext.Current.CancellationToken);
        _client = _host.GetTestClient();
        _client.BaseAddress = new Uri("http://localhost");
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            if (_host is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else
                _host.Dispose();
        }
    }

    /// <summary>A plain REST client against the same host — used to seed/verify entities out of band.</summary>
    public HttpClient CreateClient() => _client ?? throw new InvalidOperationException("MCP harness not initialized.");

    /// <summary>Invokes an MCP tool through the real RPC handler and returns the serialized CallToolResult.</summary>
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

    /// <summary>
    /// Resolves a tool's wire name from the registry by entity display name + operation kind — robust to
    /// the tool-naming convention so a capability test never has to hardcode the kebab/prefix format.
    /// </summary>
    public string ResolveToolName(string entityName, EntityEndpointOperationKind operation)
    {
        var registry = Services.GetRequiredService<McpEntityRegistry>();
        foreach (var registration in registry.Registrations)
        {
            if (!string.Equals(registration.DisplayName, entityName, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var tool in registration.Tools)
            {
                if (tool.Operation == operation) return tool.Name;
            }
        }

        throw new InvalidOperationException($"No '{operation}' tool was discovered for entity '{entityName}'.");
    }

    /// <summary>The first text content block of a CallToolResult (the serialized payload), if present.</summary>
    public static string? ContentText(JToken callResult) => callResult["content"]?[0]?["text"]?.Value<string>();

    /// <summary>True when the CallToolResult is flagged as an error.</summary>
    public static bool IsError(JToken callResult) => callResult["isError"]?.Value<bool>() ?? false;
}
