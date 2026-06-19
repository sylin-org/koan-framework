using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.Extensions;

namespace Koan.Web.Sort.Tests;

/// <summary>
/// ARCH-0091: xUnit v3 runs out-of-process and must own the assembly entry point, so this can't be a
/// <c>WebApplicationFactory&lt;Program&gt;</c>. Boots an in-memory TestServer host directly; the empty
/// <c>Configure</c> delegates the pipeline to <c>KoanWebStartupFilter</c>, and
/// <c>AddKoanControllersFrom&lt;WidgetController&gt;()</c> registers this assembly's sort controllers
/// (Widget / WidgetDefaultSort / WidgetLenient).
/// </summary>
public sealed class SortWebApplicationFactory : IAsyncLifetime
{
    private IHost? _host;

    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

    public HttpClient CreateClient()
    {
        var client = _host!.GetTestClient();
        client.BaseAddress = new Uri("http://localhost");
        return client;
    }

    public async ValueTask InitializeAsync()
    {
        _host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment("Test");
                web.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Test",
                    ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                    ["Koan:Data:Sources:Default:ConnectionString"] = "memory://sort-tests",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                }))
                .ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddKoan();
                    services.AddKoanControllersFrom<WidgetController>();
                })
                .Configure(_ => { });
            })
            .StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
