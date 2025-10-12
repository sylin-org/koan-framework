using Koan.Testing;
using Koan.Mcp.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Koan.Mcp.Extensions;
using Koan.Web.Extensions;
using Koan.Mcp.Options;
using Koan.Core;
using Koan.Mcp.TestHost.Models;
using Koan.Mcp.TestHost.Controllers;
using Koan.Web.Controllers;
using Koan.Mcp;
using Microsoft.AspNetCore.Builder;
using Koan.Web.Endpoints;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Http;
using Koan.Data.Abstractions; // for IEntity<>
using Microsoft.Extensions.Options; // for IOptions<>
using System.Reflection; // for reflection to access internal provider

namespace Koan.Samples.McpCodeMode.Tests;

public class TestPipelineFixture : KoanTestPipelineFixtureBase
{
    public TestPipelineFixture() : base(typeof(Program)) { }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
    services.AddKoan().AsProxiedApi();
        services.AddKoanMcp();
        services.AddKoanWeb();
    // No need to register KoanSdkBindings; executor constructs it per invocation.
        services.Configure<McpServerOptions>(o =>
        {
            o.Exposure = McpExposureMode.Full;
            o.EnableHttpSseTransport = true;
        });
        // Controllers from test host assembly
        services.AddKoanControllersFrom<TodosController>();
        // Override descriptor provider to inject sample sets & relationships for union type generation tests
        services.AddSingleton<IEntityEndpointDescriptorProvider, TestEntityEndpointDescriptorProvider>();
    }

    protected override void ConfigureApp(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseCors();
        app.UseEndpoints(endpoints =>
        {
            // Direct JSON-RPC endpoint for test determinism (bypasses SSE queue semantics)
            endpoints.MapPost("/mcp/direct-rpc", async ctx =>
            {
                using var sr = new StreamReader(ctx.Request.Body);
                var body = await sr.ReadToEndAsync();
                var root = JToken.Parse(body);
                var method = root["method"]?.Value<string>();
                var id = root["id"]?.ToString() ?? "null";
                var server = ctx.RequestServices.GetRequiredService<Koan.Mcp.Hosting.McpServer>();
                var handler = server.CreateHandler();
                object? result = null;
                if (method == "tools/list")
                {
                    var list = await handler.ListToolsAsync(ctx.RequestAborted);
                    result = JToken.FromObject(list!);
                }
                else if (method == "tools/call")
                {
                    var @params = root["params"] ?? new JObject();
                    var name = @params["name"]!.Value<string>()!;
                    JObject? arguments = @params["arguments"] as JObject;
                    var callParams = new Koan.Mcp.Hosting.McpRpcHandler.ToolsCallParams { Name = name, Arguments = arguments };
                    var callResult = await handler.CallToolAsync(callParams, ctx.RequestAborted);
                    if (callResult.Success && callResult.Result is not null)
                    {
                        result = callResult.Result; // flatten inner result payload (matches tests expectation)
                    }
                    else
                    {
                        result = JToken.FromObject(callResult);
                    }
                }
                else
                {
                    ctx.Response.StatusCode = 400;
                    var err = JsonConvert.SerializeObject(new { jsonrpc = "2.0", error = new { code = -32601, message = "Method not found" }, id });
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(err, ctx.RequestAborted);
                    return;
                }
                var payload = JsonConvert.SerializeObject(new { jsonrpc = "2.0", result, id });
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(payload, ctx.RequestAborted);
            });
            endpoints.MapKoanMcpEndpoints();
            endpoints.MapControllers();
        });
    }

    // Helper moved to McpFixtureExtensions for reuse.
}

internal sealed class TestEntityEndpointDescriptorProvider : IEntityEndpointDescriptorProvider
{
    private readonly object _inner; // hold internal provider instance
    private readonly MethodInfo _describeGeneric;
    private readonly MethodInfo _describeNonGeneric;

    public TestEntityEndpointDescriptorProvider(IOptions<EntityEndpointOptions> opts)
    {
        // DefaultEntityEndpointDescriptorProvider is internal; construct via reflection
        var providerType = typeof(EntityEndpointDescriptor).Assembly
            .GetTypes()
            .First(t => t.Name == "DefaultEntityEndpointDescriptorProvider");
        _inner = Activator.CreateInstance(providerType, opts)!;
        _describeGeneric = providerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Describe" && m.IsGenericMethodDefinition);
        _describeNonGeneric = providerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Describe" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
    }

    public EntityEndpointDescriptor Describe<TEntity, TKey>() where TEntity : class, IEntity<TKey> where TKey : notnull
    {
        var mi = _describeGeneric.MakeGenericMethod(typeof(TEntity), typeof(TKey));
        var result = (EntityEndpointDescriptor)mi.Invoke(_inner, Array.Empty<object?>())!;
        return Enrich(result);
    }

    public EntityEndpointDescriptor Describe(Type entityType, Type keyType)
    {
        var result = (EntityEndpointDescriptor)_describeNonGeneric.Invoke(_inner, new object?[] { entityType, keyType })!;
        return Enrich(result);
    }

    private EntityEndpointDescriptor Enrich(EntityEndpointDescriptor descriptor)
    {
        // Only enrich Todo entity for tests
        if (descriptor.EntityType.Name == "Todo")
        {
            var meta = descriptor.Metadata;
            // Use reflection to create a new metadata instance with added properties since original is immutable init-only
            var enriched = new EntityEndpointDescriptorMetadata
            {
                DefaultPageSize = meta.DefaultPageSize,
                MaxPageSize = meta.MaxPageSize,
                DefaultView = meta.DefaultView,
                AllowRelationshipExpansion = true,
                AllowedShapes = meta.AllowedShapes,
                AvailableSets = new List<string> { "default", "tenant-a", "tenant-b" },
                RelationshipNames = new List<string> { "assignedUser", "tags" }
            };
            return new EntityEndpointDescriptor(descriptor.EntityType, descriptor.KeyType, descriptor.Operations, enriched);
        }
        return descriptor;
    }
}
