using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Web.AdapterSurface.TestKit;
using Koan.Web.AdapterSurface.TestKit.Containers;

namespace Koan.Web.AdapterSurface.Mongo.Tests;

/// <summary>
/// The original-bug adapter: EntityController&lt;Widget&gt; (with a nested Sightings collection)
/// backed by Mongo, exercised over a full HTTP pipeline.
/// </summary>
public sealed class MongoAdapterFactory : WebApplicationFactory<Program>, IAdapterTestFactory
{
    private readonly MongoContainerHelper _mongo = new();
    private bool _initialized;

    public bool IsAvailable => _mongo.IsAvailable;
    public string? UnavailableReason => _mongo.UnavailableReason;
    public HttpClient Client => _mongo.IsAvailable ? CreateClient() : new HttpClient();
    public new IServiceProvider Services => base.Services;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await _mongo.InitializeAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _mongo.DisposeAsync();
    }

    public Task ResetAsync() => _mongo.ResetAsync();

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
                    ["Koan:Data:Sources:Default:Adapter"] = "mongo",
                    ["Koan:Data:Sources:Default:ConnectionString"] = _mongo.ConnectionString,
                    ["Koan:Data:Sources:Default:Database"] = _mongo.Database,
                    ["Koan:Data:Mongo:ConnectionString"] = _mongo.ConnectionString,
                    ["Koan:Data:Mongo:Database"] = _mongo.Database,
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                });
            });
            webBuilder.ConfigureServices(_ =>
            {
                Koan.Core.Hosting.App.AppHost.Current = null;
            });
        });

        var host = builder.Build();
        host.Start();
        return host;
    }
}
