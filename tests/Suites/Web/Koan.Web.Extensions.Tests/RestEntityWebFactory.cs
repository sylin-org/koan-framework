using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.App;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// ARCH-0091 / ARCH-0092: boots a direct in-memory TestServer host (no WebApplicationFactory — xUnit v3
/// owns the entry point). <c>AddKoan()</c> runs reflective discovery (which fires
/// <c>KoanWebExtensionsAutoRegistrar</c> → <c>[RestEntity]</c> registration) and
/// <c>AddKoanControllersFrom&lt;CogController&gt;()</c> registers this assembly's ApplicationPart so the
/// explicit <see cref="CogController"/> is materialized by MVC; the empty <c>Configure</c> delegates the
/// pipeline to <c>KoanWebStartupFilter</c>.
/// </summary>
public sealed class RestEntityWebFactory : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;

    public HttpClient Client => _client ?? throw new InvalidOperationException("Host not started.");

    public async ValueTask InitializeAsync()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment("Development");
                web.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Development",
                    ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                    ["Koan:Data:Sources:Default:ConnectionString"] = "memory://rest-entity-tests",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                }));
                web.ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddKoan();
                    services.AddKoanControllersFrom<CogController>();
                });
                web.Configure(_ => { });
            });

        _host = await builder.StartAsync(TestContext.Current.CancellationToken);
        _client = _host.GetTestClient();
        _client.BaseAddress = new Uri("http://localhost");
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
