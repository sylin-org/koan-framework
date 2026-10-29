using System;
using System.Collections.Generic;
using System.Net.Http;
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
using Koan.Web.Extensions;
using Xunit;
using static Koan.Web.Auth.Server.IntegrationTests.OAuthTestFlows;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0023 — an application that names an issuer keeps it: <c>Koan:Security:Trust:Issuer</c>, or the authorization
/// server's own <c>Koan:Web:Auth:Server:Issuer</c>, is the issuer the metadata states and every token carries. Each case
/// starts its own host.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class NamedIssuerSpec
{
    [Theory]
    [InlineData("Koan:Security:Trust:Issuer", "https://trust.example", "https://trust.example")]
    [InlineData("Koan:Web:Auth:Server:Issuer", "https://server.example/", "https://server.example")]
    public async Task An_application_that_names_an_issuer_keeps_it_in_the_metadata_and_the_tokens(string key, string value, string expected)
    {
        using var host = await StartNamed(key, value);
        using var http = host.GetTestClient();

        var issuer = await MetadataIssuer(http, "");
        var token = host.Services.GetRequiredService<IIssuer>().Issue(new TrustClaims { Subject = "alice" }, TimeSpan.FromMinutes(5), audience: "https://api.example");

        issuer.Should().Be(expected);
        Read(token).Issuer.Should().Be(expected);
        host.Services.GetRequiredService<IIssuer>().TryValidate(token, out _).Should().BeTrue();
        await host.StopAsync();
    }

    private static async Task<IHost> StartNamed(string key, string value)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment("Development");
                web.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Development",
                    ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                    ["Koan:Data:Sources:Default:ConnectionString"] = "memory://named-issuer",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                    [key] = value,
                }));
                web.ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddKoan();
                    services.AddKoanWeb();
                });
                web.Configure(_ => { });
            });
        return await builder.StartAsync(Ct);
    }
}
