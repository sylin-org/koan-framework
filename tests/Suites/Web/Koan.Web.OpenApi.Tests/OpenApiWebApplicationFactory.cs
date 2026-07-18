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
/// Boots an isolated TestServer host for each OpenAPI posture under test.
/// </summary>
public sealed class OpenApiWebApplicationFactory : IAsyncLifetime
{
    private readonly List<IHost> _hosts = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public HttpClient CreateClient(params (string Key, string Value)[] extraSettings)
        => CreateClientForEnvironment("Test", extraSettings);

    public HttpClient CreateClientForEnvironment(
        string environmentName,
        params (string Key, string Value)[] extraSettings)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment(environmentName);
                web.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Koan:Environment"] = environmentName,
                        ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                        ["Koan:Data:Sources:Default:ConnectionString"] = "memory://openapi-tests",
                        ["Koan:BackgroundServices:Enabled"] = "false",
                        ["Logging:LogLevel:Default"] = "Warning",
                    });
                    if (extraSettings.Length > 0)
                    {
                        cfg.AddInMemoryCollection(extraSettings.Select(
                            setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)));
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
            try
            {
                await host.StopAsync();
            }
            catch
            {
                // Best-effort fixture cleanup.
            }

            host.Dispose();
        }

        _hosts.Clear();
    }
}
