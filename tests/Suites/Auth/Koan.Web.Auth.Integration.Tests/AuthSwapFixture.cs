using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Web.Extensions;
using Xunit;

namespace Koan.Web.Auth.Integration.Tests;

/// <summary>
/// Boots a REAL Kestrel host on a loopback port (Development) with the full Koan auth fabric + the dev Test
/// provider (oauth2 <c>test</c> + oidc <c>test-oidc</c>). Real Kestrel (not TestServer) so the maintained
/// OAuth/OIDC handler's server-side Backchannel — token/userinfo/discovery/JWKS — works over real HTTP, which
/// is exactly what the engine swap (WEB-0071) delegates to the handler. <c>ASPNETCORE_URLS</c> is deliberately
/// NOT set (reproducing a container/proxy deployment), so the Test provider's relative endpoints must resolve from
/// the live request host at challenge time — the Bug-2 regression guard.
/// </summary>
public sealed class AuthSwapFixture : IAsyncLifetime
{
    private IHost? _host;
    private string? _priorUrls;

    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

    public async ValueTask InitializeAsync()
    {
        Port = GrabFreePort();
        _priorUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        // Reproduce the real deployment: the server binds (via UseUrls below) but ASPNETCORE_URLS is NOT readable by
        // the process (a container / Kestrel-config / chiseled image). The self-hosted Test provider endpoints must
        // therefore resolve from the LIVE request host, not the env var. This is the Bug-2 regression guard.
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // offline-only, mirrors the bootstrap specs
                ["Koan:Data:Redis:ConnectionString"] = "localhost:0",
                // Deliberately NO TestProvider:Enabled opt-in — this fixture runs in plain Development, exactly like
                // the real deployment that filed the regression. The Test simulator endpoints must AUTO-MAP in
                // Development (advertise ⇒ map, shared IsActive predicate); if they don't, every round-trip below
                // 404s. This is the Bug-1 regression guard against advertise/map gating drift.
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(BaseUrl);
                web.UseEnvironment("Development");
                web.ConfigureServices(s =>
                {
                    s.AddKoan();
                    s.AddKoanWeb();
                    s.AddKoanControllersFrom<WhoAmIController>();
                });
                // KoanWebStartupFilter builds the real routing → authn → authz → endpoints pipeline.
                web.Configure(_ => { });
            })
            .Build();

        await _host.StartAsync();
    }

    /// <summary>A fresh cookie-aware, non-auto-redirecting client pre-seeded with the dev persona cookie
    /// (so the Test authorize endpoint issues a code without the interactive login page).</summary>
    public HttpClient NewClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        handler.CookieContainer.Add(new Uri(BaseUrl),
            new Cookie("_tp_user", Uri.EscapeDataString("alice|alice@example.com")) { Path = "/" });
        return new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
    }

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", _priorUrls);
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
