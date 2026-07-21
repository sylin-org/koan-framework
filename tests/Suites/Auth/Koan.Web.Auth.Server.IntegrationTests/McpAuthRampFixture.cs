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
using Koan.Mcp.Options;
using Koan.Web.Extensions;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0006 Phase 1 — boots a REAL Kestrel host (Development, loopback) with the full Koan composition through
/// <c>AddKoan()</c>: the trust fabric (ES256 issuer), the cookie session, the embedded AS leaf
/// (<c>Koan.Web.Auth.Server</c> → <c>/oauth/dev-token</c>), and the MCP HTTP/SSE edge with
/// <c>RequireAuthentication</c> on. This is the ARCH-0079 composition that proves the on-ramp end-to-end:
/// cookie → dev-token (ES256, aud-bound) → bearer → the MCP edge gates.
/// </summary>
public sealed class McpAuthRampFixture : IAsyncLifetime
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
                ["ConnectionStrings:Redis"] = "localhost:0", // offline-only, mirrors the bootstrap specs
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(BaseUrl);
                web.UseEnvironment("Development");
                web.ConfigureServices(s =>
                {
                    s.AddKoan();
                    s.AddKoanWeb();
                    s.AddKoanControllersFrom<TestSignInController>();
                    // Turn the legacy MCP HTTP/SSE edge on and require auth so the SEC-0006 gates engage even in
                    // Development. (AI-0037: /sse+/rpc is now the explicit legacy opt-in; Streamable is the default.)
                    s.Configure<McpServerOptions>(o =>
                    {
                        o.EnableLegacySseTransport = true;
                        o.RequireAuthentication = true;
                    });
                });
                web.Configure(_ => { });
            })
            .Build();

        await _host.StartAsync();
    }

    /// <summary>A fresh cookie-aware, non-auto-redirecting client.</summary>
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
