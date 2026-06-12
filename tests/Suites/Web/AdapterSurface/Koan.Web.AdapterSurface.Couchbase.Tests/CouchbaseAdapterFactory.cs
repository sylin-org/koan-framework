using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Web.AdapterSurface.TestKit;
using Koan.Web.AdapterSurface.TestKit.Containers;

namespace Koan.Web.AdapterSurface.Couchbase.Tests;

public sealed class CouchbaseAdapterFactory : WebApplicationFactory<Program>, IAdapterTestFactory
{
    private readonly CouchbaseContainerHelper _couchbase = new();
    private bool _initialized;

    public bool IsAvailable => _couchbase.IsAvailable;
    public string? UnavailableReason => _couchbase.UnavailableReason;
    public HttpClient Client => _couchbase.IsAvailable ? CreateClient() : new HttpClient();
    public new IServiceProvider Services => base.Services;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await _couchbase.InitializeAsync();
    }

    async Task IAsyncLifetime.DisposeAsync() => await _couchbase.DisposeAsync();

    public Task ResetAsync() => _couchbase.ResetAsync();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseContentRoot(AppContext.BaseDirectory);
            webBuilder.UseTestServer();
            webBuilder.UseEnvironment("Development");
            webBuilder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Development",
                    ["Koan:Data:Sources:Default:Adapter"] = "couchbase",
                    ["Koan:Data:Sources:Default:ConnectionString"] = _couchbase.ConnectionString,
                    ["Koan:Data:Couchbase:ConnectionString"] = _couchbase.ConnectionString,
                    ["Koan:Data:Couchbase:ManagementUrl"] = _couchbase.ManagementUrl,
                    ["Koan:Data:Couchbase:Bucket"] = _couchbase.Bucket,
                    ["Koan:Data:Couchbase:Username"] = _couchbase.Username,
                    ["Koan:Data:Couchbase:Password"] = _couchbase.Password,
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
