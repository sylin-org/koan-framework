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
/// SEC-0006 Phase 2 — a real Kestrel host (Development, loopback) with the embedded AS + the cookie session +
/// an in-memory data adapter so the OAuth entities (client, consent request, authorization code) persist. Drives
/// the full Authorization Code + PKCE flow over real HTTP with cookies + redirects.
/// </summary>
public sealed class OAuthFlowFixture : IAsyncLifetime
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
                ["Koan:Data:Sources:Default:ConnectionString"] = "memory://oauth-flow-tests",
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
