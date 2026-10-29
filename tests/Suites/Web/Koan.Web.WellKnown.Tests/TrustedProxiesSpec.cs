using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// WEB-0078: <c>Koan:Web:TrustedProxies</c> names the reverse proxies whose <c>X-Forwarded-Proto</c> and
/// <c>X-Forwarded-For</c> Koan believes, for one hop, at the head of its pipeline. Nothing declared means no header
/// is honoured (fail-closed, like <c>Koan:Web:Origin</c>). A real Kestrel loopback host, so the connection has the
/// remote address the forwarded-headers middleware checks against the declared proxies.
/// </summary>
public sealed class TrustedProxiesSpec
{
    [Fact]
    public async Task A_declared_proxy_sets_the_scheme_and_the_client_address_for_one_hop()
    {
        await using var host = await ProxiedHost.Start(new Dictionary<string, string?> { ["Koan:Web:TrustedProxies:0"] = "127.0.0.1" });

        var oneHop = await host.Probe(("X-Forwarded-Proto", "https"), ("X-Forwarded-For", "203.0.113.9"));
        oneHop.Scheme.Should().Be("https");
        oneHop.Remote.Should().Be("203.0.113.9");

        var twoHops = await host.Probe(("X-Forwarded-Proto", "https"), ("X-Forwarded-For", "203.0.113.9, 198.51.100.1"));
        twoHops.Remote.Should().Be("198.51.100.1", "only the hop the declared proxy appended is believed");
    }

    [Fact]
    public async Task A_network_declaration_covers_the_proxy_inside_it()
    {
        await using var host = await ProxiedHost.Start(new Dictionary<string, string?> { ["Koan:Web:TrustedProxies:0"] = "127.0.0.0/8" });

        var probe = await host.Probe(("X-Forwarded-Proto", "https"), ("X-Forwarded-For", "203.0.113.9"));
        probe.Scheme.Should().Be("https");
        probe.Remote.Should().Be("203.0.113.9");
    }

    [Fact]
    public async Task Without_a_declaration_forwarded_headers_are_ignored()
    {
        await using var host = await ProxiedHost.Start(new Dictionary<string, string?>());

        var probe = await host.Probe(("X-Forwarded-Proto", "https"), ("X-Forwarded-For", "203.0.113.9"));
        probe.Scheme.Should().Be("http");
        probe.Remote.Should().Be("127.0.0.1");
    }

    [Fact]
    public async Task A_declaration_that_does_not_cover_the_caller_is_not_believed()
    {
        await using var host = await ProxiedHost.Start(new Dictionary<string, string?> { ["Koan:Web:TrustedProxies:0"] = "10.0.0.0/8" });

        var probe = await host.Probe(("X-Forwarded-Proto", "https"), ("X-Forwarded-For", "203.0.113.9"));
        probe.Scheme.Should().Be("http");
        probe.Remote.Should().Be("127.0.0.1");
    }

    [Fact]
    public async Task A_declaration_that_is_not_an_address_or_network_stops_boot_with_a_correction()
    {
        var start = () => ProxiedHost.Start(new Dictionary<string, string?> { ["Koan:Web:TrustedProxies:0"] = "the-proxy" });

        var failure = await start.Should().ThrowAsync<Exception>();
        Messages(failure.Which).Should().Contain(message => message.Contains("Koan:Web:TrustedProxies") && message.Contains("the-proxy"));
    }

    private static IEnumerable<string> Messages(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
        }
    }

    internal sealed record Observed(string Scheme, string? Remote);

    internal sealed class ProxiedHost : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly string _baseUrl;

        private ProxiedHost(IHost host, string baseUrl)
        {
            _host = host;
            _baseUrl = baseUrl;
        }

        public static async Task<ProxiedHost> Start(IReadOnlyDictionary<string, string?> settings)
        {
            var baseUrl = $"http://127.0.0.1:{FreePort()}";
            var host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Test",
                    ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                }).AddInMemoryCollection(settings))
                .ConfigureWebHostDefaults(web =>
                {
                    web.UseUrls(baseUrl);
                    web.UseEnvironment("Test");
                    web.UseContentRoot(AppContext.BaseDirectory);
                    web.ConfigureServices(services =>
                    {
                        services.AddKoan();
                        services.AddKoanControllersFrom<RequestOriginProbeController>();
                    });
                    web.Configure(_ => { });
                })
                .Build();
            try
            {
                await host.StartAsync(TestContext.Current.CancellationToken);
            }
            catch
            {
                host.Dispose();
                throw;
            }
            return new ProxiedHost(host, baseUrl);
        }

        public async Task<Observed> Probe(params (string Name, string Value)[] headers)
        {
            using var client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
            using var request = new HttpRequestMessage(HttpMethod.Get, RequestOriginProbeController.Path);
            foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return new Observed(
                document.RootElement.GetProperty("scheme").GetString()!,
                document.RootElement.GetProperty("remote").GetString());
        }

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync(CancellationToken.None);
            _host.Dispose();
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }
    }
}

/// <summary>Reports what the pipeline believes about the request after Koan's forwarded-header posture ran.</summary>
[ApiController]
public sealed class RequestOriginProbeController : ControllerBase
{
    public const string Path = "/request-origin-probe";

    [HttpGet(Path)]
    public IActionResult Get() => Ok(new { scheme = Request.Scheme, remote = HttpContext.Connection.RemoteIpAddress?.ToString() });
}
