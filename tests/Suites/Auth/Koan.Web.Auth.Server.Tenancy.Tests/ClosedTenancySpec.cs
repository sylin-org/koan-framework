using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Security.Trust.Issuer;
using Koan.Tenancy;
using Koan.Web.Auth.Server.Keys;
using Koan.Web.Auth.Server.Protocol;
using Koan.Web.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.Auth.Server.Tenancy.Tests;

/// <summary>
/// The authorization server under a Closed tenancy posture (SEC-0033): it starts with no tenant in scope, its protocol rows
/// are the host's, and its sweep reaches a person's expired rows in every tenant.
/// </summary>
public sealed class ClosedTenancySpec
{
    private static async Task<IHost> Boot(string environment, string database)
    {
        var host = Host.CreateDefaultBuilder()
            .UseEnvironment(environment)
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment(environment);
                web.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = environment,
                    ["Koan:Tenancy:Posture"] = "Closed",
                    ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                    ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={database}",
                    ["Logging:LogLevel:Default"] = "Warning",
                }));
                web.ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddKoan();
                });
                web.Configure(_ => { });
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private static string Database() => Path.Combine(Path.GetTempPath(), $"koan-auth-tenancy-{Guid.NewGuid():n}.db");

    [Fact(DisplayName = "The server starts with no tenant in scope, on a persisted signing key the next start reads back")]
    public async Task Starts_on_a_persisted_key()
    {
        var database = Database();
        string kid;
        using (var first = await Boot("Production", database))
        {
            first.Services.GetRequiredService<IIssuerKeyStore>().Should().BeOfType<PersistedIssuerKeyStore>();
            kid = (await IssuerSigningKeyRecord.All(TestContext.Current.CancellationToken)).Single(key => key.IsActive).Id;
            await first.StopAsync(TestContext.Current.CancellationToken);
        }
        using var second = await Boot("Production", database);
        (await IssuerSigningKeyRecord.All(TestContext.Current.CancellationToken)).Single(key => key.IsActive).Id.Should().Be(kid);
        await second.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "A development host seeds its dev client with no tenant in scope")]
    public async Task Seeds_the_dev_client()
    {
        using var host = await Boot("Development", Database());
        (await OAuthClient.Get(AuthServerModule.DevExplorerClientId, TestContext.Current.CancellationToken)).Should().NotBeNull();
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "The sweep removes what expired in every tenant and in the host, and nothing live")]
    public async Task Sweep_reaches_every_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = await Boot("Production", Database());
        var scopes = host.Services.GetRequiredService<IMaintenanceScopes>();
        var now = DateTimeOffset.UtcNow;

        await new TenantRecord { Id = "a", Name = "A" }.Save(ct);
        await new TenantRecord { Id = "b", Name = "B" }.Save(ct);
        foreach (var tenant in new[] { "a", "b" })
        {
            using (Tenant.Use(tenant))
            {
                await new AgentGrant { Id = $"{tenant}-expired", Subject = "p", ClientId = "c", ExpiresAt = now.AddMinutes(-1) }.Save(ct);
                await new AgentGrant { Id = $"{tenant}-live", Subject = "p", ClientId = "c", ExpiresAt = now.AddMinutes(1) }.Save(ct);
                await new RememberedConsent { Id = $"{tenant}-expired", ExpiresUtc = now.AddMinutes(-1) }.Save(ct);
            }
        }
        await new DeviceCode { Id = "expired", ExpiresUtc = now.AddMinutes(-1) }.Save(ct);

        await OAuthArtifactCleanupService.Sweep(scopes, now, ct);

        (await DeviceCode.Get("expired", ct)).Should().BeNull();
        foreach (var tenant in new[] { "a", "b" })
        {
            using (Tenant.Use(tenant))
            {
                (await AgentGrant.All(ct)).Select(grant => grant.Id).Should().Equal($"{tenant}-live");
                (await RememberedConsent.All(ct)).Should().BeEmpty();
            }
        }
        await host.StopAsync(ct);
    }

    [Fact(DisplayName = "A person's grant still needs a tenant: reading one with none in scope fails closed")]
    public async Task A_grant_stays_in_its_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = await Boot("Production", Database());
        await FluentActions.Awaiting(() => AgentGrant.All(ct)).Should().ThrowAsync<SegmentationRequiredException>();
        await host.StopAsync(ct);
    }
}
