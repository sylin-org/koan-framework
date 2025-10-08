using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Testing;

/// <summary>
/// Minimal shared test fixture that boots an in-memory ASP.NET Core host for a given <c>Program</c> entry point
/// and exposes an HttpClient. This replaces the previously referenced but non-existent KoanTestPipelineFixtureBase.
/// </summary>
public abstract class KoanTestPipelineFixtureBase : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;
    protected IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

    protected Type ProgramType { get; }

    protected KoanTestPipelineFixtureBase(Type programType) => ProgramType = programType;

    protected virtual void ConfigureAppConfiguration(IConfigurationBuilder builder) { }
    protected virtual void ConfigureTestServices(IServiceCollection services) { }
    protected virtual void ConfigureApp(IApplicationBuilder app) { }

    public HttpClient CreateClient() => _client ?? throw new InvalidOperationException("Fixture not initialized");

    public async Task InitializeAsync()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(ConfigureAppConfiguration)
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment("Development");
                web.ConfigureServices(s =>
                {
                    ConfigureTestServices(s);
                });
                web.Configure(app =>
                {
                    ConfigureApp(app);
                });
            });

        _host = await builder.StartAsync();
        _client = _host.GetTestClient();
        _client.BaseAddress = new Uri("http://localhost");
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
