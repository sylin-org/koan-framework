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
using Koan.Web.Extensions;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0027 — a real Kestrel host (Development, in-memory data) under the base URL a subclass names, with the settings
/// it adds. Every request goes to <see cref="ListenUrl"/> plus <see cref="PathBase"/>.
/// </summary>
public abstract class BasePathFixture : IAsyncLifetime
{
    /// <summary>The configured <c>Koan:Web:ApplicationUrl</c>.</summary>
    public abstract string ApplicationUrl { get; }

    /// <summary>The base URL's path.</summary>
    public string PathBase => new Uri(ApplicationUrl).AbsolutePath.TrimEnd('/');

    /// <summary>Settings beside the base URL.</summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>();

    private IHost? _host;

    public int Port { get; private set; }
    public string ListenUrl => $"http://127.0.0.1:{Port}";
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

    public async ValueTask InitializeAsync()
    {
        Port = GrabFreePort();

        var configuration = new Dictionary<string, string?>
        {
            ["Koan:Environment"] = "Development",
            ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
            ["Koan:Data:Sources:Default:ConnectionString"] = "memory://base-path-tests",
            ["Koan:BackgroundServices:Enabled"] = "false",
            ["Koan:Web:ApplicationUrl"] = ApplicationUrl,
        };
        foreach (var (key, value) in Settings) configuration[key] = value;

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(configuration))
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
                });
                web.Configure(_ => { });
            })
            .Build();

        await _host.StartAsync();
    }

    /// <summary>A fresh non-redirecting client with its own cookie jar, a person's browser.</summary>
    public HttpClient NewClient()
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

/// <summary>
/// SEC-0028 — a host under <c>https://www.example.com/notes</c> whose application renders its own consent and done pages
/// at <c>/consent</c> and <c>/consent/done</c> (<see cref="TestConsentPageController"/>) from Koan's in-process answer.
/// The per-address user-code allowance is <see cref="UserCodeLimit"/>.
/// </summary>
public class ConsentPageFixture : BasePathFixture
{
    public const string Url = "https://www.example.com/notes";
    public const string Base = "/notes";

    public override string ApplicationUrl => Url;

    /// <summary>User-code attempts allowed per address per minute.</summary>
    protected virtual int UserCodeLimit => 100;

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Koan:Web:Auth:Server:ConsentPath"] = "/consent",
        ["Koan:Web:Auth:Server:DonePath"] = "/consent/done",
        ["Koan:Web:Auth:Server:UserCodeVerificationRateLimitPerMinute"] = UserCodeLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
