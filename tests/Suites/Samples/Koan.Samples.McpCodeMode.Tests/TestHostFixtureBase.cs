using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Samples.McpCodeMode.Tests;

/// <summary>
/// ARCH-0091: local xUnit-v3 port of the retired bespoke <c>Koan.Testing.KoanTestPipelineFixtureBase</c>.
/// Boots an in-memory ASP.NET Core TestServer host (UseTestServer); subclasses configure services and
/// the application pipeline. The constructor's <paramref name="programType"/> is retained for call-site
/// parity with the bespoke base and is otherwise unused.
/// </summary>
public abstract class TestHostFixtureBase : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;

    protected IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

    protected Type ProgramType { get; }

    protected TestHostFixtureBase(Type programType) => ProgramType = programType;

    protected virtual void ConfigureAppConfiguration(IConfigurationBuilder builder) { }
    protected virtual void ConfigureTestServices(IServiceCollection services) { }
    protected virtual void ConfigureApp(IApplicationBuilder app) { }

    public HttpClient CreateClient() => _client ?? throw new InvalidOperationException("Fixture not initialized");

    public async ValueTask InitializeAsync()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(ConfigureAppConfiguration)
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment("Development");
                web.ConfigureServices(ConfigureTestServices);
                web.Configure(ConfigureApp);
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
            if (_host is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else
                _host.Dispose();
        }
    }
}
