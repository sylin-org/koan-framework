using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.Extensions;
using Xunit;

namespace Koan.Web.AdapterSurface.TestKit;

/// <summary>
/// ARCH-0091: shared base for the per-adapter test factories. xUnit v3 runs the test assembly
/// out-of-process and must own the assembly entry point, which rules out
/// <c>WebApplicationFactory&lt;Program&gt;</c> (its <c>HostFactoryResolver</c> needs the minimal-API
/// <c>Program</c> to BE the entry point). Instead this base boots an in-memory TestServer host directly
/// — <c>AddKoan()</c> for reflective discovery + <c>AddKoanControllersFrom&lt;WidgetController&gt;()</c>
/// for the surface controller, with the empty <c>Configure</c> delegating the pipeline to
/// <c>KoanWebStartupFilter</c>. Subclasses supply only the adapter config, backing-store lifecycle,
/// availability, and reset.
/// </summary>
public abstract class AdapterTestFactoryBase : IAdapterTestFactory
{
    private IHost? _host;
    private HttpClient? _client;

    /// <summary>True when the backing infrastructure (Docker, local instance, env var) is reachable.</summary>
    public abstract bool IsAvailable { get; }

    /// <summary>Human-readable reason when <see cref="IsAvailable"/> is false.</summary>
    public virtual string? UnavailableReason => null;

    public HttpClient Client => _client ?? new HttpClient();
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Adapter host not started");

    /// <summary>ASP.NET host environment. Non-production so the relational DDL guard allows AutoCreate.</summary>
    protected virtual string HostEnvironment => "Development";

    /// <summary>Adapter-specific Koan/data configuration (connection string, ddl policy, etc.).</summary>
    protected abstract IEnumerable<KeyValuePair<string, string?>> AdapterConfiguration();

    /// <summary>Start the backing store (container/dir) before the host boots. No-op by default.</summary>
    protected virtual ValueTask StartBackingStoreAsync() => ValueTask.CompletedTask;

    /// <summary>Tear down the backing store after the host stops. No-op by default.</summary>
    protected virtual ValueTask StopBackingStoreAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Adapter-specific service registrations layered on top of the shared host (e.g. extra controllers
    /// declared in the concrete test assembly). The old WebApplicationFactory model auto-discovered the
    /// entry assembly's controllers; with a direct host these must be added explicitly. No-op by default.
    /// </summary>
    protected virtual void ConfigureAdditionalServices(IServiceCollection services) { }

    /// <summary>Wipes the backing store. Called by tests between scenarios for isolation.</summary>
    public abstract Task ResetAsync();

    public async ValueTask InitializeAsync()
    {
        await StartBackingStoreAsync();
        if (!IsAvailable) return;

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment(HostEnvironment);
                web.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(AdapterConfiguration()));
                web.ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddKoan();
                    services.AddKoanControllersFrom<WidgetController>();
                    ConfigureAdditionalServices(services);
                });
                web.Configure(_ => { });
            });

        _host = await builder.StartAsync(TestContext.Current.CancellationToken);
        _client = _host.GetTestClient();
        _client.BaseAddress = new Uri("http://localhost");
    }

    public virtual async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await StopBackingStoreAsync();
    }
}
