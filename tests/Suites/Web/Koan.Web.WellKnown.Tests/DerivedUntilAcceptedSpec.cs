using System.Net;
using AwesomeAssertions;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// WEB-0081 / ARCH-0128: an application whose <see cref="IApplicationUrlStore"/> declares
/// <see cref="IApplicationUrlStore.DerivesUntilAccepted"/> consents to request-derived absolute URLs in Production, while
/// nothing is configured or accepted. Koan says so once, naming the posture and how it ends; the first accepted URL ends
/// it; a configured URL makes it moot; without it Production refuses as WEB-0078 decided.
/// </summary>
public sealed class DerivedUntilAcceptedSpec
{
    [Fact]
    public async Task In_production_the_posture_answers_the_request_origin_as_a_declared_proxy_resolved_it()
    {
        await using var host = await TrustedProxiesSpec.ProxiedHost.Start(
            new Dictionary<string, string?> { ["Koan:Web:TrustedProxies:0"] = "127.0.0.1" },
            environment: "Production",
            configure: Probe(new PostureStore(null)));

        var proxied = await host.Get(BaseUrlProbeController.Path,
            ("X-Forwarded-Proto", "https"), ("X-Forwarded-Host", "tangent.example.org"), ("X-Forwarded-For", "203.0.113.9"));
        proxied.Status.Should().Be(HttpStatusCode.OK);
        proxied.Body.Should().Be("https://tangent.example.org");

        var direct = await host.Get(BaseUrlProbeController.Path);
        direct.Body.Should().Be(host.ListenUrl, "without forwarded headers the origin is the one the request reached");
    }

    [Fact]
    public async Task In_production_an_undeclared_proxy_does_not_choose_the_origin()
    {
        await using var host = await TrustedProxiesSpec.ProxiedHost.Start(
            new Dictionary<string, string?> { ["Koan:Web:TrustedProxies:0"] = "10.0.0.0/8" },
            environment: "Production",
            configure: Probe(new PostureStore(null)));

        var answer = await host.Get(BaseUrlProbeController.Path,
            ("X-Forwarded-Proto", "https"), ("X-Forwarded-Host", "attacker.example"), ("X-Forwarded-For", "203.0.113.9"));

        answer.Body.Should().Be(host.ListenUrl);
    }

    [Fact]
    public void Without_the_posture_production_refuses_as_before()
    {
        var baseUrl = new KoanWebBaseUrl(null, Production, store: new MemoryApplicationUrlStore(null));

        var resolve = () => baseUrl.Resolve(Request("https", "attacker.example"));

        resolve.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Production").And.Contain("Koan:Web:ApplicationUrl");
        baseUrl.DerivesFromRequests.Should().BeFalse();
    }

    [Fact]
    public async Task Once_a_url_is_accepted_it_is_the_answer_whatever_the_host()
    {
        var log = new ListLogger();
        var baseUrl = new KoanWebBaseUrl(null, Production, log, new PostureStore(null));

        baseUrl.Resolve(Request("https", "first.example")).Should().Be("https://first.example");
        baseUrl.DerivesFromRequests.Should().BeTrue();

        await baseUrl.Accept("https://first.example", TestContext.Current.CancellationToken);

        baseUrl.Resolve(Request("https", "attacker.example")).Should().Be("https://first.example");
        baseUrl.Resolve(Request("http", "10.0.0.7:5080")).Should().Be("https://first.example");
        baseUrl.DerivesFromRequests.Should().BeFalse("the window closes at the first accepted URL");
    }

    [Fact]
    public async Task A_url_accepted_before_start_closes_the_window_from_the_first_request()
    {
        await using var host = await TrustedProxiesSpec.ProxiedHost.Start(
            new Dictionary<string, string?>(),
            environment: "Production",
            configure: Probe(new PostureStore("https://place.example")));

        (await host.Get(BaseUrlProbeController.Path, ("Host", "attacker.example"))).Body.Should().Be("https://place.example");
    }

    [Fact]
    public void With_a_configured_url_the_posture_has_no_effect()
    {
        var log = new ListLogger();
        var baseUrl = new KoanWebBaseUrl("https://configured.example", Production, log, new PostureStore(null));

        baseUrl.Resolve(Request("https", "attacker.example")).Should().Be("https://configured.example");
        baseUrl.DerivesFromRequests.Should().BeFalse();
        log.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Koan_says_so_once_naming_the_posture_and_how_it_ends()
    {
        var log = new ListLogger();
        var baseUrl = new KoanWebBaseUrl(null, Production, log, new PostureStore(null));

        baseUrl.Resolve(Request("https", "first.example"));
        baseUrl.Resolve(Request("https", "second.example"));

        log.Warnings.Should().ContainSingle().Which.Should()
            .Contain(nameof(IApplicationUrlStore.DerivesUntilAccepted))
            .And.Contain(typeof(PostureStore).FullName!)
            .And.Contain("Production")
            .And.Contain("accepts its URL")
            .And.Contain("Koan:Web:ApplicationUrl");
    }

    private static readonly IHostEnvironment Production = new EnvironmentStub("Production");

    private static Action<IServiceCollection> Probe(IApplicationUrlStore store) => services =>
    {
        services.AddSingleton(store);
        services.AddKoanControllersFrom<BaseUrlProbeController>();
    };

    private static HttpRequest Request(string scheme, string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        context.Request.Path = "/";
        return context.Request;
    }

    /// <summary>An application's store that declares the until-accepted posture.</summary>
    public sealed class PostureStore(string? value) : IApplicationUrlStore
    {
        private string? _value = value;

        public bool DerivesUntilAccepted => true;

        public Task<string?> Load(CancellationToken ct) => Task.FromResult(_value);

        public Task Save(string applicationUrl, CancellationToken ct)
        {
            _value = applicationUrl;
            return Task.CompletedTask;
        }
    }

    private sealed class EnvironmentStub(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = nameof(DerivedUntilAcceptedSpec);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ListLogger : ILogger<KoanWebBaseUrl>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
