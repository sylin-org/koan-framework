using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Security.Trust.Issuer;
using Koan.Web.Auth.Server.Keys;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0022 — in Development the signing keys persist when the application names a persistent key ring folder
/// (WEB-0079), and stay ephemeral when it names none. Two hosts start one after the other over the same durable data
/// folder (the JSON adapter), the way a local install restarts: with a key ring folder the second host publishes the
/// first host's key and accepts the token it issued; without one the second host has a new key and refuses it.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class DevelopmentSigningKeySpec
{
    private const string ApplicationUrl = "https://keys.example.test";

    [Fact]
    public async Task Signing_keys_survive_a_restart_in_Development_when_a_key_ring_folder_is_named()
    {
        var root = NewFolder();
        try
        {
            var data = Path.Combine(root, "data");
            var ring = Path.Combine(root, "ring");

            var (kid, token) = await IssueOnce(data, ring, expectPersisted: true);

            using var second = await Start(data, ring);
            var issuer = second.Services.GetRequiredService<IIssuer>();
            issuer.PublishedKeys.Select(key => key.Kid).Should().Contain(kid, "the restarted host loads the persisted key");
            issuer.TryValidate(token, out var principal).Should().BeTrue("a token issued before the restart still verifies");
            principal.Claims.Should().Contain(claim => claim.Value == "alice", "the verified token still names its subject");
            await second.StopAsync();
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Signing_keys_do_not_survive_a_restart_in_Development_without_a_key_ring_folder()
    {
        var root = NewFolder();
        try
        {
            var data = Path.Combine(root, "data");

            var (kid, token) = await IssueOnce(data, keyRing: null, expectPersisted: false);

            using var second = await Start(data, keyRing: null);
            var issuer = second.Services.GetRequiredService<IIssuer>();
            issuer.PublishedKeys.Select(key => key.Kid).Should().NotContain(kid, "Development without a key ring folder keeps an ephemeral key");
            issuer.TryValidate(token, out _).Should().BeFalse();
            await second.StopAsync();
        }
        finally { TryDelete(root); }
    }

    private static async Task<(string Kid, string Token)> IssueOnce(string data, string? keyRing, bool expectPersisted)
    {
        using var first = await Start(data, keyRing);
        var store = first.Services.GetRequiredService<IIssuerKeyStore>();
        if (expectPersisted) store.Should().BeOfType<PersistedIssuerKeyStore>();
        else store.Should().BeOfType<EphemeralIssuerKeyStore>();

        var issuer = first.Services.GetRequiredService<IIssuer>();
        var kid = issuer.PublishedKeys.Should().ContainSingle().Subject.Kid;
        var token = issuer.Issue(new TrustClaims { Subject = "alice" }, TimeSpan.FromMinutes(5), audience: ApplicationUrl);
        await first.StopAsync();
        return (kid, token);
    }

    private static async Task<IHost> Start(string data, string? keyRing)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Koan:Environment"] = "Development",
            ["Koan:Data:Sources:Default:Adapter"] = "json",
            ["Koan:Data:Sources:Default:json:DirectoryPath"] = data,
            ["Koan:BackgroundServices:Enabled"] = "false",
            ["Koan:Web:ApplicationUrl"] = ApplicationUrl,
            ["Logging:LogLevel:Default"] = "Warning",
        };
        if (keyRing is not null) settings["Koan:Web:KeyRingDirectory"] = keyRing;

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment("Development");
                web.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings));
                web.ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddKoan();
                });
                web.Configure(_ => { });
            });
        return await builder.StartAsync(TestContext.Current.CancellationToken);
    }

    private static string NewFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "koan-k6-keys-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
