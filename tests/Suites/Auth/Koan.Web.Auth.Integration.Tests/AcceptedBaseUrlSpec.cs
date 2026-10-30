using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.Auth.Integration.Tests;

/// <summary>
/// WEB-0081 — the Development Test IdP issues under the URL the application accepted, and under the next one it
/// accepts from the next request on, with no restart.
/// </summary>
public sealed class AcceptedBaseUrlSpec : IClassFixture<AcceptedBaseUrlFixture>
{
    private readonly AcceptedBaseUrlFixture _fx;
    public AcceptedBaseUrlSpec(AcceptedBaseUrlFixture fx) => _fx = fx;

    [Fact]
    public async Task The_test_provider_issues_under_the_accepted_url_and_follows_a_new_one()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_fx.BaseUrl) };

        (await Issuer(client)).Should().Be("https://first.example/.testoauth");

        await _fx.Services.GetRequiredService<KoanWebBaseUrl>().Accept("https://second.example", TestContext.Current.CancellationToken);

        (await Issuer(client)).Should().Be("https://second.example/.testoauth");
    }

    private static async Task<string?> Issuer(HttpClient client)
    {
        using var response = await client.GetAsync("/.testoauth/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("issuer").GetString();
    }
}

/// <summary>
/// Real Kestrel in Development with the dev Test provider and no <c>Koan:Web:ApplicationUrl</c>; the application keeps
/// its accepted URL in an <see cref="IApplicationUrlStore"/> holding <c>https://first.example</c>.
/// </summary>
public sealed class AcceptedBaseUrlFixture : IAsyncLifetime
{
    private IHost? _host;

    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

    public async ValueTask InitializeAsync()
    {
        Port = GrabFreePort();

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:0",
                ["Logging:LogLevel:Default"] = "Warning",
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(BaseUrl);
                web.UseEnvironment("Development");
                web.ConfigureServices(s =>
                {
                    s.AddDataProtection().UseEphemeralDataProtectionProvider();
                    s.AddSingleton<IApplicationUrlStore>(new MemoryApplicationUrlStore("https://first.example"));
                    s.AddKoan();
                    s.AddKoanWeb();
                });
                web.Configure(_ => { });
            })
            .Build();

        await _host.StartAsync();
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

    private sealed class MemoryApplicationUrlStore(string? value) : IApplicationUrlStore
    {
        private string? _value = value;

        public Task<string?> Load(CancellationToken ct) => Task.FromResult(_value);

        public Task Save(string applicationUrl, CancellationToken ct)
        {
            _value = applicationUrl;
            return Task.CompletedTask;
        }
    }
}
