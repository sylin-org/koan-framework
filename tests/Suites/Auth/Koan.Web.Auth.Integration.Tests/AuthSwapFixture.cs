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
/// is exactly what the engine swap (WEB-0071) delegates to the handler. <c>ASPNETCORE_URLS</c> is set so the
/// scheme seeder resolves the Test provider's relative endpoints + OIDC Authority to in-network absolute URLs.
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
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", BaseUrl);

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // offline-only, mirrors the bootstrap specs
                ["Koan:Data:Redis:ConnectionString"] = "localhost:0",
                // Map the Test provider's OAuth/OIDC simulator endpoints (opt-in, separate from the
                // Development provider auto-enable — SEC-0003). Required to exercise the real flow.
                ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
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
