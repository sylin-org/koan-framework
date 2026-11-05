using System.Net;
using System.Net.Sockets;
using Koan.Core;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Identity.Logins.Tests;

/// <summary>
/// A real Koan application on a loopback Kestrel port, in Development, with Koan.Identity over SQLite and the Test
/// connector's two providers: <c>test</c> (OAuth2) and <c>test-oidc</c> (OIDC). Real Kestrel, because the maintained
/// handlers reach the Test provider's token and userinfo endpoints over HTTP. <c>ReConsent:ForceOnLink</c> is on, and the
/// clock can be moved forward, so a sign-in can grow old.
/// </summary>
public sealed class TestConnectorHost : IAsyncLifetime
{
    private IHost? _host;
    private string? _priorUrls;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "koan-logins-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClientHandler _handler = new() { AllowAutoRedirect = false, UseCookies = false };

    public ShiftedClock Clock { get; } = new();
    public int Port { get; private set; }
    public Uri Origin => new($"http://127.0.0.1:{Port}/");
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started.");

    public async ValueTask InitializeAsync()
    {
        Port = FreePort();
        Directory.CreateDirectory(_folder);
        // The Test provider's relative endpoints resolve from the live request host, as in a container.
        _priorUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);
        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:0",
                ["Koan:BackgroundServices:Enabled"] = "false",
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={Path.Combine(_folder, "store.sqlite")};Pooling=False",
                ["Koan:Identity:SeedDevUsers"] = "false",
                ["Koan:Web:Auth:ReConsent:ForceOnLink"] = "true",
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls($"http://127.0.0.1:{Port}");
                web.UseEnvironment("Development");
                web.ConfigureServices(services =>
                {
                    services.AddSingleton<TimeProvider>(Clock);
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                    services.AddKoan();
                    services.AddKoanControllersFrom<LoginProbeController>();
                });
                web.Configure(_ => { });
            })
            .Build();
        await _host.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A browser whose Test provider persona is <paramref name="email"/>.</summary>
    public Browser Browser(string email)
    {
        var browser = new Browser(_handler, Origin);
        browser.Cookie("_tp_user", Persona(email));
        return browser;
    }

    public static string Persona(string email) => Uri.EscapeDataString($"{email.Split('@')[0]}|{email}");

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", _priorUrls);
        if (_host is not null)
        {
            await _host.StopAsync(CancellationToken.None);
            _host.Dispose();
        }
        _handler.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}

/// <summary>The real clock, moved forward by <see cref="Offset"/>; timers keep real durations.</summary>
public sealed class ShiftedClock : TimeProvider
{
    public TimeSpan Offset { get; set; }

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
}
