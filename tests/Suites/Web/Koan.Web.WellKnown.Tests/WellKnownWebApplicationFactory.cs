using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Web.WellKnown.Tests;

public sealed class WellKnownWebApplicationFactory : WebApplicationFactory<Program>
{
    // tests/Directory.Build.props redirects BaseOutputPath into %TEMP%, so the upstream
    // WebApplicationFactory's solution-root probe in SetContentRoot fails. We bypass it by
    // taking over CreateHost: build the host ourselves with explicit content root + test server.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseContentRoot(AppContext.BaseDirectory);
            webBuilder.UseTestServer();
            webBuilder.UseEnvironment("Test");

            webBuilder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Test",
                    ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                    ["Koan:Data:Sources:Default:ConnectionString"] = "memory://wellknown-tests",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                });
            });

            webBuilder.ConfigureServices(_ =>
            {
                Koan.Core.Hosting.App.AppHost.Current = null;
            });
        });

        var host = builder.Build();
        host.Start();
        return host;
    }
}
