using System.Net;
using AwesomeAssertions;
using Koan.Core;
using Koan.Web.Auth.Atproto.Tests;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Identity.Logins.Tests;

/// <summary>
/// A real Koan application (TestServer) at <c>https://app.example</c>, in Development, with Koan.Identity over SQLite and
/// the atproto connector, whose accounts live on a loopback PDS that is also their PLC directory.
/// </summary>
internal sealed class AtprotoHost : IAsyncDisposable
{
    public static readonly Uri Public = new("https://app.example/");
    private readonly IHost _host;
    private readonly string _folder;
    private readonly HttpMessageHandler _handler;

    private AtprotoHost(IHost host, string folder, LoopbackPds pds)
    {
        _host = host;
        _folder = folder;
        Pds = pds;
        _handler = host.GetTestServer().CreateHandler();
    }

    public LoopbackPds Pds { get; }
    public IServiceProvider Services => _host.Services;

    /// <summary>A did:plc in its exact form, every one of its 24 characters <paramref name="letter"/>.</summary>
    public static string Did(char letter) => "did:plc:" + new string(letter, 24);

    public static string HandleOf(string did) => did[^6..] + ".test";

    public static async Task<AtprotoHost> Start(params string[] dids)
    {
        var pds = await LoopbackPds.Start();
        foreach (var did in dids) pds.Account(did, HandleOf(did));
        var folder = Path.Combine(Path.GetTempPath(), "koan-logins-at-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var settings = new Dictionary<string, string?>
        {
            ["Koan:Environment"] = "Development",
            ["Koan:BackgroundServices:Enabled"] = "false",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Koan:Identity:SeedDevUsers"] = "false",
            ["Koan:Web:ApplicationUrl"] = Public.ToString().TrimEnd('/'),
            ["Koan:Web:Auth:Providers:atproto:Scopes:0"] = "atproto",
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={Path.Combine(folder, "store.sqlite")};Pooling=False",
            ["Koan:Web:Auth:Atproto:DevelopmentAllowedOrigins:0"] = pds.Origin,
            ["Koan:Web:Auth:Atproto:DevelopmentPlcDirectory"] = pds.Origin,
        };
        foreach (var did in dids) settings["Koan:Web:Auth:Atproto:DevelopmentHandles:" + HandleOf(did)] = did;

        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment("Development");
                web.UseContentRoot(AppContext.BaseDirectory);
                web.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
                web.ConfigureServices(services =>
                {
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                    services.AddKoan();
                    services.AddKoanControllersFrom<LoginProbeController>();
                });
                web.Configure(_ => { });
            })
            .Build();
        try { await host.StartAsync(TestContext.Current.CancellationToken); }
        catch
        {
            host.Dispose();
            await pds.DisposeAsync();
            throw;
        }
        return new AtprotoHost(host, folder, pds);
    }

    public Browser Browser() => new(_handler, new Uri("http://localhost/"), Public);

    /// <summary>A sign-in of <paramref name="did"/>: the challenge, the provider's approval, and the callback.</summary>
    public Task<Who> SignIn(Browser browser, string did)
        => Approve(browser, $"/auth/atproto/challenge?identifier={HandleOf(did)}&return={LoginProbeController.WhoAmI}", did);

    /// <summary>Binds <paramref name="did"/>'s login to the person <paramref name="browser"/> is signed in as.</summary>
    public Task<Who> Bind(Browser browser, string did)
        => Approve(browser, $"/me/connections/atproto/link?identifier={HandleOf(did)}&return={LoginProbeController.WhoAmI}", did);

    private async Task<Who> Approve(Browser browser, string challenge, string did)
    {
        var started = await browser.Go(challenge);
        started.Status.Should().Be(HttpStatusCode.Redirect, started.Trace);
        started.At.ToString().Should().StartWith(Pds.Origin + "/authorize?", started.Trace);
        var landed = await browser.Go("/auth/atproto/callback" + Pds.Approval(LoopbackPds.StateOf(started.At.ToString()), did));
        landed.Status.Should().Be(HttpStatusCode.OK, landed.Trace);
        landed.At.AbsolutePath.Should().Be(LoginProbeController.WhoAmI, landed.Trace);
        return await browser.WhoAmI();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync(CancellationToken.None);
        _host.Dispose();
        _handler.Dispose();
        await Pds.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
