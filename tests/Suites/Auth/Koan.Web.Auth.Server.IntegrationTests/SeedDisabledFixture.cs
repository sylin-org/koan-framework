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
/// SEC-0006 addendum (WEB-0072 P3) — a Development host with the dev-client seed turned <b>off</b>
/// (<c>Koan:Web:Auth:Server:SeedDevClient=false</c>). Proves the operator opt-out: the env gate would allow the
/// seed here, so the absent client isolates the knob's effect. Its own in-memory store keeps it from seeing a
/// <c>koan-dev-explorer</c> seeded by any other host's connection string.
/// </summary>
public sealed class SeedDisabledFixture : IAsyncLifetime
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
                ["Koan:Environment"] = "Development",
                ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                ["Koan:Data:Sources:Default:ConnectionString"] = "memory://dev-seed-disabled",
                ["Koan:BackgroundServices:Enabled"] = "false",
                ["Koan:Web:Auth:Server:SeedDevClient"] = "false",
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(BaseUrl);
                web.UseEnvironment("Development");
                web.ConfigureServices(s =>
                {
                    AppHost.Current = null;
                    s.AddKoan();
                    s.AddKoanWeb();
                    s.AddKoanControllersFrom<TestSignInController>();
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
