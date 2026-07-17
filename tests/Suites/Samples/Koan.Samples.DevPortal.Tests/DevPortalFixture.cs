using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using DevPortal.Controllers;
using DevPortal.Initialization;

namespace Koan.Samples.DevPortal.Tests;

public sealed class DevPortalFixture : IAsyncLifetime
{
    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "koan-s10-tests",
        Guid.NewGuid().ToString("N"));
    private IHost? _host;

    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started.");

    public HttpClient CreateClient()
    {
        var client = _host!.GetTestClient();
        client.BaseAddress = new Uri("http://localhost");
        return client;
    }

    public async ValueTask InitializeAsync()
    {
        _ = typeof(DevPortalModule);
        Directory.CreateDirectory(_dataDirectory);
        AppHost.Current = null;

        _host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .UseEnvironment("Development")
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Development",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                    ["Koan:Data:Sources:Default:ConnectionString"] =
                        $"Data Source={Path.Combine(_dataDirectory, "editorial.db")}",
                    ["Koan:Data:Sources:Preview:Adapter"] = "sqlite",
                    ["Koan:Data:Sources:Preview:ConnectionString"] =
                        $"Data Source={Path.Combine(_dataDirectory, "preview.db")}",
                    ["Koan:Data:Sources:Documents:Adapter"] = "mongo",
                    ["Koan:Data:Sources:Documents:ConnectionString"] =
                        "mongodb://127.0.0.1:1/S10?serverSelectionTimeoutMS=250&connectTimeoutMS=250",
                    ["Koan:Data:Sources:Relational:Adapter"] = "postgres",
                    ["Koan:Data:Sources:Relational:ConnectionString"] =
                        "Host=127.0.0.1;Port=1;Database=s10;Username=test;Password=test;Timeout=1;Command Timeout=1",
                    ["Koan:Web:OpenGraph:ShellPath"] = "wwwroot/index.html",
                    ["Koan:Web:OpenGraph:SiteName"] = "Koan Developer Publishing Portal",
                    ["Koan:Web:OpenGraph:DefaultDescription"] =
                        "Approve articles locally and publish them through named provider channels.",
                    ["Logging:LogLevel:Default"] = "Warning"
                }))
                .ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.AddKoanControllersFrom<PublicationController>();
                })
                .Configure(_ => { }))
            .StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
            AppHost.Current = null;
        }

        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }
}
