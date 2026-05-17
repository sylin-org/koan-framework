using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Json.Tests;

public sealed class JsonAdapterFactory : WebApplicationFactory<Program>, IAdapterTestFactory
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"koan-json-surface-{Guid.NewGuid():N}");

    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public HttpClient Client => CreateClient();
    public new IServiceProvider Services => base.Services;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_dataDir);
        return Task.CompletedTask;
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        try { Directory.Delete(_dataDir, recursive: true); } catch { }
        return Task.CompletedTask;
    }

    public async Task ResetAsync()
    {
        Koan.Core.Hosting.App.AppHost.Current = Services;
        await Widget.RemoveAll();
    }

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
                    ["Koan:Data:Sources:Default:Adapter"] = "json",
                    ["Koan:Data:Sources:Default:DirectoryPath"] = _dataDir,
                    ["Koan:Data:Json:DirectoryPath"] = _dataDir,
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
