using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.App;
using Xunit;

namespace Koan.Web.OpenApi.Tests;

/// <summary>
/// ARCH-0091: xUnit v3 runs out-of-process and must own the assembly entry point, so this can't be a
/// <c>WebApplicationFactory&lt;Program&gt;</c>. The Swagger gate is config-sensitive (each spec wants a
/// different <c>Koan:OpenApi:EnableUi</c> / legacy toggle), so instead of WAF's
/// <c>WithWebHostBuilder</c>, <see cref="CreateClient"/> boots a fresh in-memory TestServer host per call
/// with the requested overrides layered over the base config. Hosts are torn down on dispose.
/// </summary>
public sealed class SwaggerWebApplicationFactory : IAsyncLifetime
{
    private readonly List<IHost> _hosts = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Boot a fresh TestServer host (base config + per-test overrides) and return its client.</summary>
    public HttpClient CreateClient(params (string Key, string Value)[] extraSettings)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment("Test");
                web.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Koan:Environment"] = "Test",
                        ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                        ["Koan:Data:Sources:Default:ConnectionString"] = "memory://openapi-tests",
                        ["Koan:BackgroundServices:Enabled"] = "false",
                        ["Logging:LogLevel:Default"] = "Warning",
                    });
                    if (extraSettings.Length > 0)
                    {
                        cfg.AddInMemoryCollection(extraSettings.Select(
                            s => new KeyValuePair<string, string?>(s.Key, s.Value)));
                    }
                });
                web.ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddKoan();
                });
                web.Configure(_ => { });
            })
            .Build();

        host.Start();
        _hosts.Add(host);
        var client = host.GetTestClient();
        client.BaseAddress = new Uri("http://localhost");
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            try { await host.StopAsync(); } catch { /* best effort */ }
            host.Dispose();
        }
        _hosts.Clear();
    }
}
