using System.Collections.Concurrent;
using AwesomeAssertions;
using Koan.Core.Diagnostics;
using Koan.Web.Hosting;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// WEB-0081: a stored application URL this host cannot use never stops it from starting. A place set up in Development
/// keeps <c>http://127.0.0.1:5220</c> legitimately; started in Production, that value is set aside with one warning, the
/// base URL behaves as if nothing were accepted, the store is never written, and the next accept replaces it.
/// </summary>
public sealed class UnusableStoredUrlSpec
{
    private const string DevelopmentAddress = "http://127.0.0.1:5220";

    [Fact]
    public async Task In_production_with_the_posture_a_stored_loopback_url_is_set_aside_and_requests_derive()
    {
        var store = new MemoryApplicationUrlStore(DevelopmentAddress, derivesUntilAccepted: true);
        var log = new CapturedLog();
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(
            new Dictionary<string, string?>(), Probe(store, log), environment: "Production");
        using var client = host.Client;
        var baseUrl = host.Services.GetRequiredService<KoanWebBaseUrl>();

        baseUrl.Current.Should().BeNull("a stored value the host cannot use is not held");
        baseUrl.Accepted.Should().BeNull();
        baseUrl.DerivesFromRequests.Should().BeTrue();
        (await client.GetStringAsync(BaseUrlProbeController.Path)).Should().Be("http://localhost", "the request derives the base URL");

        log.Warnings(SetAside).Should().ContainSingle().Which.Should()
            .Contain(typeof(MemoryApplicationUrlStore).FullName!)
            .And.Contain(DevelopmentAddress)
            .And.Contain("outside Development")
            .And.Contain("derive from each request");
        store.Saves.Should().Be(0, "Koan never writes to the store at start");
        store.Value.Should().Be(DevelopmentAddress);

        var fact = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts
            .Should().ContainSingle(fact => fact.Code == KoanWebConstants.BaseUrl.FactCode).Subject;
        fact.ReasonCode.Should().Be(KoanWebConstants.BaseUrl.DerivedFactReason);
        fact.Summary.Should().Contain(DevelopmentAddress).And.Contain("not used");
    }

    [Fact]
    public async Task In_production_without_the_posture_the_start_succeeds_and_an_absolute_url_is_refused_where_it_is_needed()
    {
        var store = new MemoryApplicationUrlStore(DevelopmentAddress);
        var log = new CapturedLog();
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(
            new Dictionary<string, string?>(), Probe(store, log), environment: "Production");
        var baseUrl = host.Services.GetRequiredService<KoanWebBaseUrl>();

        baseUrl.Current.Should().BeNull();
        baseUrl.DerivesFromRequests.Should().BeFalse();
        var resolve = () => baseUrl.Resolve(Request("https", "attacker.example"));
        resolve.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Production").And.Contain("Koan:Web:ApplicationUrl");

        log.Warnings(SetAside).Should().ContainSingle().Which.Should()
            .Contain(DevelopmentAddress).And.Contain("refused where it is needed");
        store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task With_a_configured_url_a_stored_unusable_value_plays_no_part()
    {
        var store = new MemoryApplicationUrlStore(DevelopmentAddress);
        var log = new CapturedLog();
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(
            new Dictionary<string, string?> { ["Koan:Web:ApplicationUrl"] = "https://configured.example" },
            Probe(store, log), environment: "Production");
        using var client = host.Client;

        (await client.GetStringAsync(BaseUrlProbeController.Path)).Should().Be("https://configured.example");
        host.Services.GetRequiredService<KoanWebBaseUrl>().Accepted.Should().BeNull();
        log.WarningsFrom(typeof(KoanWebBaseUrl).FullName!).Should().BeEmpty("the configured URL wins, so the start is silent");
        store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task In_development_a_stored_loopback_url_is_still_held()
    {
        var store = new MemoryApplicationUrlStore(DevelopmentAddress);
        var log = new CapturedLog();
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(
            new Dictionary<string, string?>(), Probe(store, log), environment: "Development");
        using var client = host.Client;

        host.Services.GetRequiredService<KoanWebBaseUrl>().Accepted.Should().Be(DevelopmentAddress);
        (await client.GetStringAsync(BaseUrlProbeController.Path)).Should().Be(DevelopmentAddress);
        log.Warnings(SetAside).Should().BeEmpty();
    }

    [Fact]
    public async Task After_the_start_a_valid_accept_replaces_it()
    {
        var store = new MemoryApplicationUrlStore(DevelopmentAddress);
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(
            new Dictionary<string, string?>(), Probe(store, new CapturedLog()), environment: "Production");
        using var client = host.Client;
        var baseUrl = host.Services.GetRequiredService<KoanWebBaseUrl>();

        await baseUrl.Accept("https://place.example", TestContext.Current.CancellationToken);

        (await client.GetStringAsync(BaseUrlProbeController.Path)).Should().Be("https://place.example");
        store.Value.Should().Be("https://place.example");
        store.Saves.Should().Be(1);
        var fact = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts
            .Should().ContainSingle(fact => fact.Code == KoanWebConstants.BaseUrl.FactCode).Subject;
        fact.ReasonCode.Should().Be(KoanWebConstants.BaseUrl.AcceptedFactReason);
        fact.Summary.Should().NotContain(DevelopmentAddress);
    }

    [Fact]
    public async Task A_stored_value_that_no_longer_parses_as_an_origin_is_set_aside_too()
    {
        var store = new MemoryApplicationUrlStore("https://place.example/tangent");
        var log = new CapturedLog();
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(new Dictionary<string, string?>(), Probe(store, log));

        host.Services.GetRequiredService<KoanWebBaseUrl>().Accepted.Should().BeNull();
        log.Warnings(SetAside).Should().ContainSingle().Which.Should().Contain("path");
        store.Saves.Should().Be(0);
    }

    // The one warning a set-aside value raises; the posture's own warning at the first derived URL is another.
    private const string SetAside = "Koan does not use it";

    private static Action<IServiceCollection> Probe(IApplicationUrlStore store, CapturedLog log) => services =>
    {
        AcceptedBaseUrlSpec.Probe(store)(services);
        services.AddSingleton<ILoggerProvider>(log);
    };

    private static HttpRequest Request(string scheme, string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        context.Request.Path = "/";
        return context.Request;
    }

    /// <summary>Every warning the host logs, with its category, so a spec counts the ones it cares about.</summary>
    private sealed class CapturedLog : ILoggerProvider
    {
        private readonly ConcurrentQueue<(string Category, string Message)> _warnings = new();

        public IReadOnlyList<string> Warnings(string mention)
            => _warnings.Where(entry => entry.Message.Contains(mention)).Select(entry => entry.Message).ToList();

        public IReadOnlyList<string> WarningsFrom(string category)
            => _warnings.Where(entry => entry.Category == category).Select(entry => entry.Message).ToList();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _warnings);

        public void Dispose() { }

        private sealed class Logger(string category, ConcurrentQueue<(string, string)> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning) warnings.Enqueue((category, formatter(state, exception)));
            }
        }
    }
}
