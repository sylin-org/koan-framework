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
using Koan.Mcp.Options;
using Koan.Web.Extensions;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// WEB-0078 — a real Kestrel host whose canonical base URL (<c>Koan:Web:ApplicationUrl</c>) carries a path, with
/// no <c>Issuer</c> and no <c>ResourceUri</c> configured. The authorization server, the cookie session and the MCP
/// edge are expected to derive from the base URL: the issuer under the path, the session cookie scoped to it, the
/// MCP resource and its authorization server under it, and nothing served at the root.
/// </summary>
public sealed class BaseUrlPathFixture : IAsyncLifetime
{
    public const string ApplicationUrl = "https://www.example.com/tangent";
    public const string PathBase = "/tangent";

    private IHost? _host;

    public int Port { get; private set; }
    public string ListenUrl => $"http://127.0.0.1:{Port}";
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

    public async ValueTask InitializeAsync()
    {
        Port = GrabFreePort();

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Development",
                ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                ["Koan:Data:Sources:Default:ConnectionString"] = "memory://base-url-path-tests",
                ["Koan:BackgroundServices:Enabled"] = "false",
                ["Koan:Web:ApplicationUrl"] = ApplicationUrl,
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(ListenUrl);
                web.UseEnvironment("Development");
                web.ConfigureServices(s =>
                {
                    AppHost.Current = null;
                    s.AddKoan();
                    s.AddKoanWeb();
                    s.AddKoanControllersFrom<TestSignInController>();
                    s.Configure<McpServerOptions>(o =>
                    {
                        o.EnableStreamableHttpTransport = true;
                        o.RequireAuthentication = true;
                    });
                });
                web.Configure(_ => { });
            })
            .Build();

        await _host.StartAsync();
    }

    /// <summary>A fresh non-redirecting client that surfaces Set-Cookie headers instead of storing them.</summary>
    public HttpClient NewClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        };
        return new HttpClient(handler) { BaseAddress = new Uri(ListenUrl) };
    }

    /// <summary>A fresh non-redirecting client with its own cookie jar, for the cookie → dev-token ramp.</summary>
    public HttpClient NewCookieClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };
        return new HttpClient(handler) { BaseAddress = new Uri(ListenUrl) };
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
