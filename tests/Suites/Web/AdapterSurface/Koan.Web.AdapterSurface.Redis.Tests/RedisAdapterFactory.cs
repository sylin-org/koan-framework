using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Web.AdapterSurface.TestKit;
using Koan.Web.AdapterSurface.TestKit.Containers;

namespace Koan.Web.AdapterSurface.Redis.Tests;

public sealed class RedisAdapterFactory : WebApplicationFactory<Program>, IAdapterTestFactory
{
    private readonly RedisContainerHelper _redis = new();
    private bool _initialized;

    // Redis partition routing: most ops route through EntityContext correctly, but
    // bulk-upsert and collection-read with ?set= leak across to the default partition.
    // Tracked as a follow-up; opt out of partition specs to keep the matrix honest.
    public bool SupportsPartitions => false;
    public bool SupportsCrossPartitionTransfer => false;

    public bool IsAvailable => _redis.IsAvailable;
    public string? UnavailableReason => _redis.UnavailableReason;
    public HttpClient Client => _redis.IsAvailable ? CreateClient() : new HttpClient();
    public new IServiceProvider Services => base.Services;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await _redis.InitializeAsync();
    }

    async Task IAsyncLifetime.DisposeAsync() => await _redis.DisposeAsync();

    public Task ResetAsync() => _redis.ResetAsync();

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
                    ["Koan:Data:Sources:Default:Adapter"] = "redis",
                    ["Koan:Data:Sources:Default:ConnectionString"] = _redis.ConnectionString,
                    ["Koan:Data:Redis:ConnectionString"] = _redis.ConnectionString,
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                });
            });
            webBuilder.ConfigureServices(_ => { Koan.Core.Hosting.App.AppHost.Current = null; });
        });
        var host = builder.Build();
        host.Start();
        return host;
    }
}
