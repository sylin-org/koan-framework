using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Mcp.Options;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Mcp.Streamable.IntegrationTests;

/// <summary>
/// AI-0037 — boots a REAL Kestrel host (Development, loopback) with the full Koan composition through
/// <c>AddKoan()</c> and the MCP Streamable HTTP transport enabled. Authentication is OFF (Development, anonymous) so
/// the suite isolates the transport mechanics — session lifecycle, content negotiation, streaming, resumption —
/// from the OAuth ingress (which the SEC-0006 ramp already proves e2e). Real Kestrel (not TestServer) is required
/// because the GET stream is a long-lived chunked SSE response.
/// </summary>
public sealed class StreamableHttpFixture : IAsyncLifetime
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
                    s.Configure<McpServerOptions>(o =>
                    {
                        o.EnableStreamableHttpTransport = true;
                        o.EnableHttpSseTransport = false;
                        o.RequireAuthentication = false; // anonymous: this suite is about the transport, not auth
                    });
                });
                web.Configure(_ => { });
            })
            .Build();

        await _host.StartAsync();
    }

    public HttpClient NewClient() => new() { BaseAddress = new Uri(BaseUrl) };

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
