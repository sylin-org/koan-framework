using GardenCoop.Controllers;
using GardenCoop.Initialization;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Koan.Samples.GardenCoop.C02.Tests;

public sealed class GardenCoopC02Fixture : IAsyncLifetime
{
    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "koan-gardencoop-c02-tests",
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
        _ = typeof(GardenCoopModule);
        Directory.CreateDirectory(_dataDirectory);
        AppHost.Current = null;

        var database = Path.Combine(_dataDirectory, "gardencoop.db");
        _host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .UseEnvironment("Development")
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Development",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Koan:Data:Sqlite:ConnectionString"] = $"Data Source={database}",
                    ["Koan:Data:SqliteVec:ConnectionString"] = $"Data Source={database}",
                    ["Koan:Ai:Onnx:ModelPath"] = "models/model_quantized.onnx",
                    ["Koan:Ai:Onnx:ModelName"] = "all-MiniLM-L6-v2",
                    ["Logging:LogLevel:Default"] = "Warning"
                }))
                .ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.AddKoanControllersFrom<ProduceSearchController>();
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
            Directory.Delete(_dataDirectory, recursive: true);
    }
}
