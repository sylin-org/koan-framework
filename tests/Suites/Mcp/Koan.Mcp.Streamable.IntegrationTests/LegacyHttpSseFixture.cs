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
/// AI-0037 Phase 3 (Ph3-pre) — boots the DEPRECATED legacy HTTP+SSE transport (the 2-endpoint
/// <c>GET /sse</c> + <c>POST /rpc</c> shape) on real Kestrel, anonymous, so the legacy wire golden bytes can be
/// captured BEFORE the 3b collapse and re-asserted against the shim afterwards (the review's hard precondition:
/// there was no wire-level legacy e2e). A long keep-alive interval keeps heartbeat/keep-alive frames out of the
/// asserted frame sequence.
/// </summary>
public sealed class LegacyHttpSseFixture : IAsyncLifetime
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
                    s.Configure<McpServerOptions>(o =>
                    {
                        o.EnableHttpSseTransport = false;
                        o.EnableLegacySseTransport = true;      // the legacy transport under test
                        o.RequireAuthentication = false;
                        o.Transport.SseKeepAliveInterval = TimeSpan.FromMinutes(10); // no heartbeat during the test
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
