using Koan.Testing;
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

namespace Koan.Samples.McpCodeMode.Tests;

/// <summary>
/// Specialized fixture with deterministic CodeMode quota configuration.
/// MaxSdkCalls=2, RequireAnswer=true to enforce both error paths predictably.
/// </summary>
public class StrictQuotaTestPipelineFixture : KoanTestPipelineFixtureBase
{
    public StrictQuotaTestPipelineFixture() : base(typeof(Program)) { }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
    services.AddKoanCore();
    services.AddKoanWeb();
    services.AddKoanMcp();
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
