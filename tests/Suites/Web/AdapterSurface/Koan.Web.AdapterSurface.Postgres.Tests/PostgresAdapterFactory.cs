using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Web.AdapterSurface.TestKit;
using Koan.Web.AdapterSurface.TestKit.Containers;

namespace Koan.Web.AdapterSurface.Postgres.Tests;

public sealed class PostgresAdapterFactory : WebApplicationFactory<Program>, IAdapterTestFactory
{
    private readonly PostgresContainerHelper _pg = new();
    private bool _initialized;

    // Postgres partition routing has the same framework gap as Sqlite — see
    // Koan.Web.AdapterSurface.Sqlite.Tests.SqliteAdapterFactory for the failure shape and tracker.
    public bool SupportsPartitions => false;
    public bool SupportsCrossPartitionTransfer => false;

    public bool IsAvailable => _pg.IsAvailable;
    public string? UnavailableReason => _pg.UnavailableReason;
    public HttpClient Client => _pg.IsAvailable ? CreateClient() : new HttpClient();
    public new IServiceProvider Services => base.Services;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await _pg.InitializeAsync();
    }

    async Task IAsyncLifetime.DisposeAsync() => await _pg.DisposeAsync();

    public Task ResetAsync() => _pg.ResetAsync();

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
                    ["Koan:Data:Sources:Default:Adapter"] = "postgres",
                    ["Koan:Data:Sources:Default:ConnectionString"] = _pg.ConnectionString,
                    ["Koan:Data:Postgres:ConnectionString"] = _pg.ConnectionString,
                    ["Koan:Data:Postgres:DdlPolicy"] = "AutoCreate",
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
