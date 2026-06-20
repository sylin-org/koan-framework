using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Mcp.Hosting;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Mcp.Explorer.IntegrationTests;

/// <summary>
/// WEB-0072 — boots a REAL Kestrel host (Development, loopback) through <c>AddKoan()</c> reflective discovery
/// (ARCH-0079): the Koan.Mcp.Explorer leaf auto-registers, its endpoint contributor mounts the console via the
/// WEB-0069 single-<c>UseEndpoints</c> pipeline (empty <c>web.Configure</c>), and three test entities exercise the
/// Verb / Door / Wall states. The HTTP/SSE transport is intentionally OFF — proving the console works standalone
/// off the in-process executor.
/// </summary>
public sealed class ExplorerFixture : IAsyncLifetime
{
    private IHost? _host;

    public const string TestInstructions = "Use the trinket tools to manage trinkets.";

    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

    public async ValueTask InitializeAsync()
    {
        Port = GrabFreePort();

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Mcp:Explorer:Enabled"] = "true",
                ["Koan:Mcp:Instructions"] = TestInstructions,
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(BaseUrl);
                web.UseEnvironment("Development");
                web.ConfigureServices(s =>
                {
                    AppHost.Current = null;
                    _ = typeof(McpExplorerOptions); // force the leaf assembly to load for reflective discovery

                    s.AddKoan();
                    s.AddKoanWeb();

                    // The default authenticate scheme: an X-Test-Auth header becomes the request principal.
                    s.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

                    // StdioTransport reads stdin (dead in the test host) — drop it (mirrors the MCP fixtures).
                    var stdio = s.FirstOrDefault(d =>
                        d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(StdioTransport));
                    if (stdio is not null) s.Remove(stdio);
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
