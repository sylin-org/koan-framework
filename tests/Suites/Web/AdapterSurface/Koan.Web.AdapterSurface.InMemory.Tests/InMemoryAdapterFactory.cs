using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.InMemory.Tests;

public sealed class InMemoryAdapterFactory : WebApplicationFactory<Program>, IAdapterTestFactory
{
    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public HttpClient Client => CreateClient();
    public new IServiceProvider Services => base.Services;

    public Task InitializeAsync() => Task.CompletedTask;
    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

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
                    ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                    ["Koan:Data:Sources:Default:ConnectionString"] = "memory://adapter-surface",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                });
            });
            webBuilder.ConfigureServices(services =>
            {
                Koan.Core.Hosting.App.AppHost.Current = null;
            });
        });

        var host = builder.Build();
        host.Start();
        return host;
    }
}
