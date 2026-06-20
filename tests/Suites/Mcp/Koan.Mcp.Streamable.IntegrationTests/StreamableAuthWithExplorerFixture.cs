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
/// AI-0037 D-C / review finding 2 — Streamable + Explorer with <c>RequireAuthentication = true</c>. Proves the
/// console (the WEB-0072 anonymous-discoverable human face) is NOT bearer-gated even when auth is required, while
/// the SSE-stream branch of the same route IS — the reason the bare GET is mapped outside the auth group with
/// inline per-branch auth.
/// </summary>
public sealed class StreamableAuthWithExplorerFixture : IAsyncLifetime
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
                ["Koan:Data:Redis:ConnectionString"] = "localhost:0",
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
                        o.EnableHttpSseTransport = true;
                        o.RequireAuthentication = true; // the deployment posture the review flagged
                    });
                    s.Configure<McpExplorerOptions>(o => o.Enabled = true);
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
