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
/// Exposure=Full but <c>CodeMode:Enabled=false</c>. The kill switch must hide the code tools from
/// tools/list AND refuse koan.code.execute / koan.code.validate when called by name — discovery
/// hiding is not a security control on its own. Entity tools stay exposed (Full).
/// </summary>
public class CodeModeDisabledFixture : TestHostFixtureBase
{
    public CodeModeDisabledFixture() : base(typeof(Program)) { }

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
            o.Exposure = McpExposureMode.Full;
            o.EnableStreamableHttpTransport = false;
        });
        services.Configure<CodeModeOptions>(o =>
        {
            o.Enabled = false; // kill switch
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
