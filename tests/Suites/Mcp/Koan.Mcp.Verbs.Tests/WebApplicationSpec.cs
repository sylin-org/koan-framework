using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Mcp.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>Koan's canonical grammar, <c>WebApplication</c>: the pipeline a tool call goes through is the one this host builds, with no connection around the call.</summary>
public sealed class WebApplicationSpec
{
    [Fact]
    public async Task A_tool_call_reaches_its_route_and_answers_as_a_request_does()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-verbs-app-{Guid.CreateVersion7():n}.db");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            AppHost.Current = null;
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.Configuration["Koan:Data:Sources:Default:Adapter"] = "sqlite";
            builder.Configuration["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path};Pooling=False";
            builder.Configuration["Koan:Mcp:EnableStdioTransport"] = "false";
            builder.WebHost.UseTestServer();
            builder.Services.AddScoped<TenantHolder>();
            builder.Services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, TenantMiddleware>();
            builder.Services.AddAuthorization(options => options.AddPolicy(Forum.NobodyPasses, policy => policy.RequireAssertion(_ => false)));
            builder.Services.AddKoan();
            await using var app = builder.Build();
            await app.StartAsync(ct);

            using var client = app.GetTestClient();
            var request = await client.GetStringAsync("/fidelity/tenant", ct);
            using var scope = app.Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<McpServer>().CreateHandler();
            var result = await handler.CallTool(new McpRpcHandler.ToolsCallParams { Name = "tenant_get", Arguments = new JObject() }, ct);

            result.IsError.Should().BeFalse(result.Content[0].Text);
            result.Content[0].Text.Should().Be(request);
            await app.StopAsync(ct);
        }
        finally
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }
}
