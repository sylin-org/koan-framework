using Koan.Mcp.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Koan.Mcp.Extensions;
using Koan.Web.Extensions;
using Koan.Mcp.Options;
using Microsoft.AspNetCore.Builder;
using Koan.Mcp.TestHost.Controllers;
using Koan.Mcp.CodeMode.Execution; // for CodeModeOptions
using Koan.Mcp; // for exposure enum
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core; // for AddKoan extension
using Microsoft.Extensions.Hosting;

namespace Koan.Samples.McpCodeMode.Tests;

/// <summary>
/// Specialized fixture with deterministic CodeMode quota configuration.
/// MaxSdkCalls=2, RequireAnswer=true to enforce both error paths predictably.
/// </summary>
public class StrictQuotaTestPipelineFixture : TestHostFixtureBase
{
    public StrictQuotaTestPipelineFixture() : base(typeof(Program)) { }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddKoan().AsProxiedApi();
        services.AddKoanWeb();
        services.AddKoanMcp();

        var stdioService = services.FirstOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(Koan.Mcp.Hosting.StdioTransport));
        if (stdioService != null)
        {
            services.Remove(stdioService);
        }

        services.Configure<McpServerOptions>(o =>
        {
            o.Exposure = McpExposureMode.Full;
            o.EnableHttpSseTransport = false; // keep deterministic
        });
        services.Configure<CodeModeOptions>(o =>
        {
            o.Enabled = true;
            o.MaxSdkCalls = 2; // deterministic low ceiling
            o.RequireAnswer = true;
        });
        services.AddKoanControllersFrom<TodosController>();
    }

    protected override void ConfigureApp(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(e =>
        {
            e.MapKoanMcpEndpoints();
            e.MapControllers();
        });
    }

    // Helper now provided via McpFixtureExtensions.InvokeRpcAsync extension.
}
