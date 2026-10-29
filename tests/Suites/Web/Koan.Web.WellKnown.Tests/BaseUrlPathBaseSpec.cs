using System.Net;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Web.Hosting;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// WEB-0078: <c>Koan:Web:ApplicationUrl</c> is the one canonical base URL. When it carries a path, Koan serves the
/// application under that path and nothing at the root — the health controller, the health alias and the well-known
/// documents included. The value is held in two forms: the URL string with its path percent-encoded (lowercase
/// scheme and host, the port only when it is not the default, no trailing slash) and the decoded path base the
/// pipeline matches. The runtime facts state the base URL the host serves under.
/// </summary>
public sealed class BaseUrlPathBaseSpec
{
    [Theory]
    [InlineData("https://www.example.com/tangent", "https://www.example.com/tangent", "/tangent", "/tangent")]
    [InlineData("https://www.example.com/tangent/", "https://www.example.com/tangent", "/tangent", "/tangent")]
    [InlineData("HTTPS://WWW.Example.COM/tangent", "https://www.example.com/tangent", "/tangent", "/tangent")]
    [InlineData("https://www.example.com:443/tangent", "https://www.example.com/tangent", "/tangent", "/tangent")]
    [InlineData("https://www.example.com:8443/tangent", "https://www.example.com:8443/tangent", "/tangent", "/tangent")]
    [InlineData("https://www.example.com/my%20space", "https://www.example.com/my%20space", "/my space", "/my%20space")]
    public async Task The_application_answers_under_its_normalised_path_and_nowhere_else(
        string configured, string normalized, string pathBase, string requestPrefix)
    {
        await using var host = await BaseUrlHost.Start(new Dictionary<string, string?>
        {
            ["Koan:Web:ApplicationUrl"] = configured,
        });
        using var client = host.Client;

        var baseUrl = host.Services.GetRequiredService<KoanWebBaseUrl>();
        baseUrl.Configured.Should().Be(normalized, "the URL form keeps its path percent-encoded");
        baseUrl.PathBase.Value.Should().Be(pathBase, "the path base is the decoded form the pipeline matches");

        (await client.GetAsync($"{requestPrefix}/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"{requestPrefix}/api/health")).StatusCode.Should().Be(HttpStatusCode.OK, "the health alias lives under the path too");
        (await client.GetAsync($"{requestPrefix}/.well-known/Koan/facts")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.NotFound, "nothing is served at the root");
        (await client.GetAsync("/api/health")).StatusCode.Should().Be(HttpStatusCode.NotFound, "the alias runs after the path base");
        (await client.GetAsync("/.well-known/Koan/facts")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"{requestPrefix}x/health/live")).StatusCode.Should().Be(HttpStatusCode.NotFound, "the path base matches whole segments");

        var fact = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts
            .Should().ContainSingle(fact => fact.Code == KoanWebConstants.BaseUrl.FactCode).Subject;
        fact.Subject.Should().Be(KoanWebConstants.BaseUrl.FactSubject);
        fact.Summary.Should().Contain(normalized).And.Contain("Koan:Web:ApplicationUrl");
    }

    [Fact]
    public async Task Without_a_path_the_root_serves_and_the_base_url_is_still_authoritative()
    {
        await using var host = await BaseUrlHost.Start(new Dictionary<string, string?>
        {
            ["Koan:Web:ApplicationUrl"] = "https://www.example.com",
        });
        using var client = host.Client;

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        host.Services.GetRequiredService<KoanWebBaseUrl>().PathBase.HasValue.Should().BeFalse();
        KoanWeb.Urls.Base(host.Services.GetRequiredService<IConfiguration>()).Should().Be("https://www.example.com");
        KoanWeb.Urls.Build("swagger", host.Services.GetRequiredService<IConfiguration>()).Should().Be("https://www.example.com/swagger");
    }

    [Fact]
    public async Task Without_a_base_url_the_fact_says_it_derives_from_each_request()
    {
        await using var host = await BaseUrlHost.Start(new Dictionary<string, string?>());
        using var client = host.Client;

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        host.Services.GetRequiredService<KoanWebBaseUrl>().IsConfigured.Should().BeFalse();

        var fact = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts
            .Should().ContainSingle(fact => fact.Code == KoanWebConstants.BaseUrl.FactCode).Subject;
        fact.Summary.Should().Contain("No base URL is configured").And.Contain("Koan:Web:ApplicationUrl");
    }

    [Theory]
    [InlineData("www.example.com/tangent", "absolute http or https")]
    [InlineData("ftp://www.example.com", "absolute http or https")]
    [InlineData("https://www.example.com/tangent?tenant=a", "query")]
    [InlineData("https://www.example.com/tangent#top", "fragment")]
    [InlineData("https://user:secret@www.example.com/tangent", "user information")]
    [InlineData("https://www.example.com/a%2Fb", "encoded separator")]
    [InlineData("http://0.0.0.0:5080", "unspecified listen address")]
    [InlineData("http://[::]:5080", "unspecified listen address")]
    public async Task A_value_that_is_not_a_public_url_stops_boot_with_a_correction(string configured, string reason)
    {
        var start = () => BaseUrlHost.Start(new Dictionary<string, string?> { ["Koan:Web:ApplicationUrl"] = configured });

        var failure = await start.Should().ThrowAsync<Exception>();
        Messages(failure.Which).Should().Contain(message =>
            message.Contains("Koan:Web:ApplicationUrl") && message.Contains("public URL") && message.Contains(reason));
    }

    private static IEnumerable<string> Messages(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    foreach (var message in Messages(inner)) yield return message;
                }
            }
        }
    }

    /// <summary>An in-memory Koan web host (TestServer) booted through <c>AddKoan()</c> with the given settings.</summary>
    internal sealed class BaseUrlHost : IAsyncDisposable
    {
        private readonly IHost _host;

        private BaseUrlHost(IHost host) => _host = host;

        public IServiceProvider Services => _host.Services;

        public HttpClient Client
        {
            get
            {
                var client = _host.GetTestClient();
                client.BaseAddress = new Uri("http://localhost");
                return client;
            }
        }

        public static async Task<BaseUrlHost> Start(
            IReadOnlyDictionary<string, string?> settings,
            Action<IServiceCollection>? configure = null,
            string environment = "Test",
            Action<IServiceCollection>? beforeKoan = null)
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.UseEnvironment(environment);
                    web.UseContentRoot(AppContext.BaseDirectory);
                    web.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Koan:Environment"] = environment,
                        ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                        ["Koan:BackgroundServices:Enabled"] = "false",
                        ["Koan:Web:ExposeObservabilitySnapshot"] = "true",
                        ["Logging:LogLevel:Default"] = "Warning",
                    }).AddInMemoryCollection(settings));
                    web.ConfigureServices(services =>
                    {
                        beforeKoan?.Invoke(services);
                        services.AddKoan();
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
                host.Dispose();
                throw;
            }
            return new BaseUrlHost(host);
        }

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync(CancellationToken.None);
            _host.Dispose();
        }
    }
}
