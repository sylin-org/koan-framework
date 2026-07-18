using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Mcp.Explorer;
using Koan.Mcp.Options;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Mcp.Streamable.IntegrationTests;

/// <summary>
/// AI-0037 D-C atomicity (review finding 6) — boots the Streamable HTTP transport AND the WEB-0072 Explorer
/// console TOGETHER (the configuration no existing fixture exercised, and the one that would have thrown
/// <c>AmbiguousMatchException</c> if both still mapped <c>GET /mcp</c>). Proves the seam: exactly one
/// <c>GET /mcp</c> resolves, routing a browser to the console and an MCP client to the stream.
/// </summary>
public sealed class StreamableWithExplorerFixture : IAsyncLifetime
{
    private IHost? _host;

    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public async ValueTask InitializeAsync()
    {
        Port = GrabFreePort();

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:0",
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(BaseUrl);
                web.UseEnvironment("Development");
                web.ConfigureServices(s =>
                {
                    s.AddKoan();
                    s.AddKoanWeb();
                    // EnableHttpSseTransport is the master switch → Streamable mounts by default.
                    s.Configure<McpServerOptions>(o =>
                    {
                        o.EnableHttpSseTransport = true;
                        o.RequireAuthentication = false;
                    });
                    s.Configure<McpExplorerOptions>(o => o.Enabled = true); // the console, co-enabled
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
