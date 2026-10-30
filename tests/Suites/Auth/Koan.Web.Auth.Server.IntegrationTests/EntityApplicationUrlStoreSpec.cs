using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// WEB-0081 — an application keeps its accepted URL on an <c>Entity&lt;T&gt;</c> row. One host accepts a URL, which its
/// store writes; the next host over the same data reads the row while it starts, after the data pillar has started, and
/// serves its first request under that URL.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class EntityApplicationUrlStoreSpec
{
    private const string Accepted = "https://place.example";

    [Fact]
    public async Task A_url_kept_on_an_entity_row_is_the_base_url_from_the_next_host_s_first_request()
    {
        var data = Path.Combine(Path.GetTempPath(), "koan-k10-entity-url-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(data);
        try
        {
            using (var first = await Start(data))
            {
                first.Services.GetRequiredService<KoanWebBaseUrl>().Accepted.Should().BeNull("no row is written yet");
                await first.Services.GetRequiredService<KoanWebBaseUrl>().Accept(Accepted, TestContext.Current.CancellationToken);
                await first.StopAsync(TestContext.Current.CancellationToken);
            }

            using var second = await Start(data);
            second.Services.GetRequiredService<KoanWebBaseUrl>().Accepted.Should().Be(Accepted);
            using var client = second.GetTestClient();
            using var response = await client.GetAsync("/.well-known/oauth-authorization-server", TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var metadata = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            metadata.RootElement.GetProperty("issuer").GetString().Should().Be(Accepted);
            await second.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            try { Directory.Delete(data, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static Task<IHost> Start(string data)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Koan:Environment"] = "Development",
            ["Koan:Data:Sources:Default:Adapter"] = "json",
            ["Koan:Data:Sources:Default:json:DirectoryPath"] = data,
            ["Koan:BackgroundServices:Enabled"] = "false",
            ["Logging:LogLevel:Default"] = "Warning",
        };
        return Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment("Development");
                web.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings));
                web.ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddSingleton<IApplicationUrlStore, PlaceAddressStore>();
                    services.AddKoan();
                });
                web.Configure(_ => { });
            })
            .StartAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>The application's settings row that holds the URL it accepted.</summary>
public sealed class PlaceAddress : Entity<PlaceAddress>
{
    public const string Row = "place";

    public string? Url { get; set; }
}

/// <summary>An application's accepted-URL store over its own <see cref="PlaceAddress"/> row.</summary>
public sealed class PlaceAddressStore : IApplicationUrlStore
{
    public async Task<string?> Load(CancellationToken ct) => (await PlaceAddress.Get(PlaceAddress.Row, ct))?.Url;

    public Task Save(string applicationUrl, CancellationToken ct)
        => new PlaceAddress { Id = PlaceAddress.Row, Url = applicationUrl }.Save(ct);
}
