using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using Koan.Core;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.Auth.Integration.Tests;

/// <summary>
/// Bootstrap fails closed: a flow handler whose start-up reconciliation fails stops the host, naming the failure,
/// rather than leaving an application serving people over state that was never reconciled.
/// </summary>
public sealed class BootstrapFailureSpec
{
    [Fact]
    public async Task A_bootstrap_failure_stops_the_host_with_the_failure()
    {
        var port = GrabFreePort();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:0",
                ["Logging:LogLevel:Default"] = "None",
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls($"http://127.0.0.1:{port}");
                web.UseEnvironment("Development");
                web.ConfigureServices(services =>
                {
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                    services.AddSingleton<BootstrapFailureMarker>();
                    services.AddKoan();
                    services.AddKoanWeb();
                });
                web.Configure(_ => { });
            })
            .Build();

        var start = () => host.StartAsync(TestContext.Current.CancellationToken);

        (await start.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(FailingBootstrap.Failure);
    }

    private static int GrabFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}

/// <summary>Present only in the host that asks its discovered handler to fail.</summary>
public sealed class BootstrapFailureMarker;

public sealed class FailingBootstrap : IKoanAuthFlowHandler
{
    public const string Failure = "The bootstrap reconciliation failed.";

    public Task OnBootstrap(AuthBootstrapContext ctx, CancellationToken ct)
        => ctx.Services.GetService<BootstrapFailureMarker>() is null
            ? Task.CompletedTask
            : throw new InvalidOperationException(Failure);
}
