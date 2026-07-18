using Microsoft.Extensions.DependencyInjection;
using Koan.Web.Extensions;
using Koan.Mcp.Options;
using Microsoft.AspNetCore.Builder;
using Koan.Mcp.TestHost.Controllers;
using Koan.Mcp;
using Koan.Core;
using Microsoft.Extensions.Hosting;

namespace Koan.Samples.McpCodeMode.Tests;

/// <summary>
/// Exposure=Tools with code mode otherwise enabled. The exposure mode hides koan.code.execute from
/// tools/list; the RPC invoke-by-name path must still refuse it, proving the gate is the exposure
/// mode itself and not merely the listing filter (the originally-reported bypass).
/// </summary>
public class ToolsOnlyExposureFixture : TestHostFixtureBase
{
    public ToolsOnlyExposureFixture() : base(typeof(Program)) { }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddKoan().AsProxiedApi();
        services.AddKoanWeb();

        var stdioService = services.FirstOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(Koan.Mcp.Hosting.StdioTransport));
        if (stdioService != null)
        {
            services.Remove(stdioService);
        }

        services.Configure<McpServerOptions>(o =>
        {
            o.Exposure = McpExposureMode.Tools; // entity tools only
            o.EnableHttpSseTransport = false;
        });
        services.Configure<CodeModeOptions>(o =>
        {
            o.Enabled = true; // enabled, but not exposed under Tools
        });
        services.AddKoanControllersFrom<TodosController>();
    }

    protected override void ConfigureApp(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(e =>
        {
            e.MapControllers();
        });
    }
}
