using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Jobs;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OrderIntake.Controllers;
using OrderIntake.Initialization;

namespace Koan.Samples.OrderIntake.Tests;

public sealed class OrderIntakeFixture : IAsyncLifetime
{
    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "koan-order-intake-tests",
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
        _ = typeof(OrderIntakeModule);
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
                        $"Data Source={Path.Combine(_dataDirectory, "control.db")}",
                    ["Koan:Data:Sources:Local:Adapter"] = "sqlite",
                    ["Koan:Data:Sources:Local:ConnectionString"] =
                        $"Data Source={Path.Combine(_dataDirectory, "local.db")}",
                    ["Koan:Data:Sources:Documents:Adapter"] = "mongo",
                    ["Koan:Data:Sources:Documents:ConnectionString"] =
                        "mongodb://127.0.0.1:1/order_intake?serverSelectionTimeoutMS=250&connectTimeoutMS=250",
                    ["Koan:Data:Sources:Relational:Adapter"] = "postgres",
                    ["Koan:Data:Sources:Relational:ConnectionString"] =
                        "Host=127.0.0.1;Port=1;Database=order_intake;Username=test;Password=test;Timeout=1;Command Timeout=1",
                    ["Koan:Data:Sources:KeyValue:Adapter"] = "redis",
                    ["Koan:Data:Sources:KeyValue:ConnectionString"] =
                        "127.0.0.1:1,connectTimeout=250,abortConnect=false",
                    ["Logging:LogLevel:Default"] = "Warning"
                }))
                .ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.AddKoanJobs(options =>
                    {
                        options.DefaultMaxAttempts = 1;
                        options.PollInterval = TimeSpan.FromMilliseconds(20);
                        options.RetryBaseDelay = TimeSpan.FromMilliseconds(10);
                    });
                    services.AddKoanControllersFrom<TrialsController>();
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
