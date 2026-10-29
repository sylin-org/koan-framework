using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.Extensions;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0025/SEC-0026 — the OAuth flow host (Kestrel, Development, loopback, in-memory data) with Web Auth's resource
/// edge composed (<c>Koan:Web:Auth:ResourceServer:Enabled</c>), so a request carrying a bearer for the person is
/// authenticated by that bearer on every ordinary request, the authorization server's included.
/// </summary>
public sealed class ResourceEdgeFlowFixture : IAsyncLifetime
{
    private IHost? _host;

    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

    public async ValueTask InitializeAsync()
    {
        Port = GrabFreePort();

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Development",
                ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                ["Koan:Data:Sources:Default:ConnectionString"] = "memory://resource-edge-flow-tests",
                ["Koan:Web:Auth:ResourceServer:Enabled"] = "true",
                ["Koan:BackgroundServices:Enabled"] = "false",
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(BaseUrl);
                web.UseEnvironment("Development");
                web.ConfigureServices(s =>
                {
                    AppHost.Current = null;
                    s.AddKoan();
                    s.AddKoanWeb();
                    s.AddKoanControllersFrom<TestSignInController>();
                });
                web.Configure(_ => { });
            })
            .Build();

        await _host.StartAsync();
    }

    /// <summary>A fresh cookie-aware, non-auto-redirecting client (so we can read Location/redirect ourselves).</summary>
    public HttpClient NewClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };
        return new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    private static int GrabFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try { return ((IPEndPoint)l.LocalEndpoint).Port; }
        finally { l.Stop(); }
    }
}
