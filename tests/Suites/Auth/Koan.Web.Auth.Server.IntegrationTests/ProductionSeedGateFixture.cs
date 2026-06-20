using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0006 addendum (WEB-0072 P3) — a <b>Production</b> host with the seed knob left at its default (<c>true</c>).
/// Proves the real security guarantee: the known/guessable <c>koan-dev-explorer</c> client must <b>never</b> exist
/// outside Development (a pre-registered public client there is a takeover vector). The env gate blocks the seed
/// even though the knob is on. Uses the persisted ES256 key store (the non-Development tier) over in-memory data.
/// </summary>
public sealed class ProductionSeedGateFixture : IAsyncLifetime
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
                ["Koan:Environment"] = "Production",
                ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                ["Koan:Data:Sources:Default:ConnectionString"] = "memory://prod-seed-gate",
                ["Koan:BackgroundServices:Enabled"] = "false",
                // Acknowledge the throwaway HS256 key so this Production test host clears the SEC-0003 boot guard
                // (we are testing the seed env gate, not the key lifecycle).
                ["Koan:Security:Trust:AllowInsecureKeyInProduction"] = "true",
                // Knob intentionally left default (true) — only the env gate should suppress the seed.
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(BaseUrl);
                web.UseEnvironment("Production");
                web.ConfigureServices(s =>
                {
                    AppHost.Current = null;
                    s.AddKoan();
                    s.AddKoanWeb();
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
