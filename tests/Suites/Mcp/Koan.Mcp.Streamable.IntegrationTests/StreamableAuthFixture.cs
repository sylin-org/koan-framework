using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Net;
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
/// AI-0037 + SEC-0006 — the same composition as <see cref="StreamableHttpFixture"/> but with the MCP edge requiring
/// authentication (the trust-fabric ES256 bearer). Used to prove the transport's central security property: the
/// <c>Mcp-Session-Id</c> is NOT a bearer capability — a non-initialize request must come from the same principal
/// that established the session, so a different authenticated caller who learns the id cannot inject RPC under the
/// owner's identity.
/// </summary>
public sealed class StreamableAuthFixture : IAsyncLifetime
{
    private IHost? _host;

    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public string Resource => $"{BaseUrl}/mcp";
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

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
                        o.EnableStreamableHttpTransport = true;
                        o.RequireAuthentication = true; // engage the SEC-0006 bearer gate + the same-principal check
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
