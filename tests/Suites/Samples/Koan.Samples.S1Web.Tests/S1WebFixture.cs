using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using S1.Web;

namespace Koan.Samples.S1Web.Tests;

public sealed class S1WebFixture : IAsyncLifetime
{
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
        AppHost.Current = null;

        _host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .UseEnvironment("Development")
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Development",
                    ["Koan:Data:Sqlite:ConnectionString"] = "Data Source=:memory:",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                }))
                .ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.AddKoanControllersFrom<TodoController>();
                })
                .Configure(_ => { }))
            .StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is null) return;
        await _host.StopAsync();
        _host.Dispose();
        AppHost.Current = null;
    }
}
