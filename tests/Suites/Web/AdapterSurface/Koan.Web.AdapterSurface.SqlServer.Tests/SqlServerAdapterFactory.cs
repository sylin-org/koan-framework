using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Web.AdapterSurface.TestKit;
using Koan.Web.AdapterSurface.TestKit.Containers;

namespace Koan.Web.AdapterSurface.SqlServer.Tests;

public sealed class SqlServerAdapterFactory : WebApplicationFactory<Program>, IAdapterTestFactory
{
    private readonly SqlServerContainerHelper _sqlServer = new();
    private bool _initialized;

    public bool IsAvailable => _sqlServer.IsAvailable;
    public string? UnavailableReason => _sqlServer.UnavailableReason;
    public HttpClient Client => _sqlServer.IsAvailable ? CreateClient() : new HttpClient();
    public new IServiceProvider Services => base.Services;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await _sqlServer.InitializeAsync();
    }

    async Task IAsyncLifetime.DisposeAsync() => await _sqlServer.DisposeAsync();

    public Task ResetAsync() => _sqlServer.ResetAsync();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseContentRoot(AppContext.BaseDirectory);
            webBuilder.UseTestServer();
            webBuilder.UseEnvironment("Development");
            webBuilder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Development",
                    ["Koan:AllowMagicInProduction"] = "true",
                    ["Koan:Data:Sources:Default:Adapter"] = "sqlserver",
                    ["Koan:Data:Sources:Default:ConnectionString"] = _sqlServer.ConnectionString,
                    ["Koan:Data:SqlServer:ConnectionString"] = _sqlServer.ConnectionString,
                    ["Koan:Data:SqlServer:DdlPolicy"] = "AutoCreate",
                    ["Koan:Data:Relational:Materialization:FailOnMismatch"] = "false",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                });
            });
            webBuilder.ConfigureServices(_ => { Koan.Core.Hosting.App.AppHost.Current = null; });
        });
        var host = builder.Build();
        host.Start();
        return host;
    }
}
