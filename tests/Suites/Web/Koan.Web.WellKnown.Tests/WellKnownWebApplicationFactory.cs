using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.App;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// ARCH-0091: xUnit v3 runs out-of-process and must own the assembly entry point, so this can't be a
/// <c>WebApplicationFactory&lt;Program&gt;</c> (its HostFactoryResolver needs the minimal-API Program to
/// BE the entry point). Boots an in-memory TestServer host directly instead; the empty <c>Configure</c>
/// delegates the pipeline to <c>KoanWebStartupFilter</c>, and the built-in WellKnown controllers come
/// from Koan.Web's own registrar.
/// </summary>
public sealed class WellKnownWebApplicationFactory : IAsyncLifetime
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
                    ["Koan:Data:Sources:Default:ConnectionString"] = "memory://wellknown-tests",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Koan:Web:ExposeObservabilitySnapshot"] = "true",
                    ["Logging:LogLevel:Default"] = "Warning",
                }))
                .ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddKoan();
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
