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
/// WEB-0078: <c>Koan:Web:TrustedProxies</c> names the reverse proxies whose <c>X-Forwarded-Proto</c>,
/// <c>X-Forwarded-Host</c> (WEB-0081) and <c>X-Forwarded-For</c> Koan believes, however many stand in a row, at the head
/// of its pipeline. Nothing declared means no header is honoured (fail-closed, like <c>Koan:Web:Origin</c>). A real Kestrel
/// loopback host, so the connection has the remote address the forwarded-headers middleware checks against the declared
/// proxies.
/// </summary>
public sealed class TrustedProxiesSpec
{
    [Fact]
    public async Task A_declared_proxy_sets_the_scheme_the_host_and_the_client_address()
    {
        await using var host = await ProxiedHost.Start(new Dictionary<string, string?> { ["Koan:Web:TrustedProxies:0"] = "127.0.0.1" });

        var oneHop = await host.Probe(("X-Forwarded-Proto", "https"), ("X-Forwarded-For", "203.0.113.9"));
        oneHop.Scheme.Should().Be("https");
        oneHop.Remote.Should().Be("203.0.113.9");
        oneHop.Host.Should().Be(host.ListenUrl["http://".Length..], "without X-Forwarded-Host the host is the one the proxy sent");

        var forwardedHost = await host.Probe(("X-Forwarded-Host", "tangent.example.org"), ("X-Forwarded-For", "203.0.113.9"));
        forwardedHost.Host.Should().Be("tangent.example.org", "a declared proxy's forwarded host is the host the request arrived on");

        var twoHops = await host.Probe(("X-Forwarded-Proto", "https"), ("X-Forwarded-For", "203.0.113.9, 198.51.100.1"));
        twoHops.Remote.Should().Be("198.51.100.1", "only the hop the declared proxy appended is believed");
    }

    // A CDN in front of a reverse proxy: every visitor must not read as the CDN's edge.
    [Fact]
    public async Task Declared_proxies_in_a_row_are_walked_back_to_the_client()
    {
        await using var host = await ProxiedHost.Start(new Dictionary<string, string?>
        {
            ["Koan:Web:TrustedProxies:0"] = "127.0.0.1",
            ["Koan:Web:TrustedProxies:1"] = "198.51.100.0/24",
        });

        var probe = await host.Probe(
            ("X-Forwarded-Proto", "https, http"),
            ("X-Forwarded-Host", "tangent.example.org, internal.example"),
            ("X-Forwarded-For", "203.0.113.9, 198.51.100.1"));

        probe.Remote.Should().Be("203.0.113.9");
        probe.Scheme.Should().Be("https");
        probe.Host.Should().Be("tangent.example.org");
    }

    [Fact]
    public async Task The_walk_stops_at_the_first_address_nobody_declared()
    {
        await using var host = await ProxiedHost.Start(new Dictionary<string, string?>
        {
            ["Koan:Web:TrustedProxies:0"] = "127.0.0.1",
            ["Koan:Web:TrustedProxies:1"] = "198.51.100.0/24",
        });

        // A client writes what it likes to the left of its own address; nothing past that address is believed.
        var probe = await host.Probe(("X-Forwarded-For", "6.6.6.6, 198.51.100.7, 203.0.113.9, 198.51.100.1"));

        probe.Remote.Should().Be("203.0.113.9");
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

        var probe = await host.Probe(("X-Forwarded-Proto", "https"), ("X-Forwarded-Host", "tangent.example.org"), ("X-Forwarded-For", "203.0.113.9"));
        probe.Scheme.Should().Be("http");
        probe.Remote.Should().Be("127.0.0.1");
        probe.Host.Should().Be(host.ListenUrl["http://".Length..]);
    }

    // A compose file that passes ${PROXY:-} declares an empty entry. ASP.NET believes every peer when it knows no proxy
    // and no network, so a blank entry must leave the posture exactly as an empty list does.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t ")]
    public async Task A_blank_declaration_declares_nothing_and_forwarded_headers_are_ignored(string blank)
    {
        await using var host = await ProxiedHost.Start(new Dictionary<string, string?>
        {
            ["Koan:Web:TrustedProxies:0"] = blank,
            ["Koan:Web:TrustedProxies:1"] = " ",
        });

        var probe = await host.Probe(("X-Forwarded-Proto", "https"), ("X-Forwarded-Host", "evil.example"), ("X-Forwarded-For", "203.0.113.9"));
        probe.Scheme.Should().Be("http");
        probe.Remote.Should().Be("127.0.0.1");
        probe.Host.Should().Be(host.ListenUrl["http://".Length..]);
    }

    [Fact]
    public async Task A_blank_entry_beside_a_declared_proxy_leaves_that_proxy_believed()
    {
        await using var host = await ProxiedHost.Start(new Dictionary<string, string?>
        {
            ["Koan:Web:TrustedProxies:0"] = "",
            ["Koan:Web:TrustedProxies:1"] = "127.0.0.1",
        });

        var probe = await host.Probe(("X-Forwarded-Proto", "https"), ("X-Forwarded-Host", "tangent.example.org"), ("X-Forwarded-For", "203.0.113.9"));
        probe.Scheme.Should().Be("https");
        probe.Host.Should().Be("tangent.example.org");
        probe.Remote.Should().Be("203.0.113.9");
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

    // A host whose services lost AddAuthorization stops at boot with the correction, not a 500 on each request.
    [Fact]
    public async Task A_host_without_authorization_services_stops_boot_naming_the_registration()
    {
        var start = () => ProxiedHost.Start(new Dictionary<string, string?>(), configure: services =>
        {
            foreach (var marker in services.Where(service => service.ServiceType.Name == "AuthorizationPolicyMarkerService").ToArray())
            {
                services.Remove(marker);
            }
        });

        var failure = await start.Should().ThrowAsync<Exception>();
        Messages(failure.Which).Should().Contain(message => message.Contains("AddAuthorization"));
    }

    private static IEnumerable<string> Messages(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
        }
    }

    internal sealed record Observed(string Scheme, string? Remote, string Host);

    internal sealed class ProxiedHost : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly string _baseUrl;

        private ProxiedHost(IHost host, string baseUrl)
        {
            _host = host;
            _baseUrl = baseUrl;
        }

        public IServiceProvider Services => _host.Services;

        public string ListenUrl => _baseUrl;

        public static async Task<ProxiedHost> Start(
            IReadOnlyDictionary<string, string?> settings,
            string environment = "Test",
            Action<IServiceCollection>? configure = null)
        {
            var baseUrl = $"http://127.0.0.1:{FreePort()}";
            var host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = environment,
                    ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                }).AddInMemoryCollection(settings))
                .ConfigureWebHostDefaults(web =>
                {
                    web.UseUrls(baseUrl);
                    web.UseEnvironment(environment);
                    web.UseContentRoot(AppContext.BaseDirectory);
                    web.ConfigureServices(services =>
                    {
                        services.AddKoan();
                        services.AddKoanControllersFrom<RequestOriginProbeController>();
                        configure?.Invoke(services);
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
                // A start that fails in module start runs after the host attached itself as AppHost.Current; stopping
                // releases that lease, so a failed start does not leave the next spec on a disposed provider.
                try { await host.StopAsync(CancellationToken.None); } catch { /* the start already failed */ }
                host.Dispose();
                throw;
            }
            return new ProxiedHost(host, baseUrl);
        }

        /// <summary>GET <paramref name="path"/> with <paramref name="headers"/>, answering the status and the body text.</summary>
        public async Task<(HttpStatusCode Status, string Body)> Get(string path, params (string Name, string Value)[] headers)
        {
            using var client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
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
                document.RootElement.GetProperty("remote").GetString(),
                document.RootElement.GetProperty("host").GetString()!);
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
    public IActionResult Get() => Ok(new { scheme = Request.Scheme, remote = HttpContext.Connection.RemoteIpAddress?.ToString(), host = Request.Host.Value });
}
