using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.Extensions;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// ARCH-0079 — a real AddKoan() TestServer host backed by the in-memory data adapter, so
/// <see cref="Keys.IssuerSigningKeyRecord"/> (Entity&lt;T&gt;) actually persists. Used to exercise the persisted
/// ES256 key store directly (it is data-layer behaviour, not env-dependent, so the host runs in Development to
/// avoid unrelated production boot guards). A web host (TestServer) is required because AddKoan() pulls in the
/// MVC/web services.
/// </summary>
public sealed class PersistedKeyFixture : IAsyncLifetime
{
    private IHost? _host;

    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

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
                    ["Koan:Data:Sources:Default:ConnectionString"] = "memory://persisted-key-tests",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                }));
                web.ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddKoan();
                });
                web.Configure(_ => { });
            });

        _host = await builder.StartAsync(TestContext.Current.CancellationToken);
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
