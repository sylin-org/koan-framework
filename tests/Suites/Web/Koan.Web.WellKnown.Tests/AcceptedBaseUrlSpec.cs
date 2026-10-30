using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// WEB-0081: an application that supplies an <see cref="IApplicationUrlStore"/> gives its base URL a value at runtime.
/// Precedence is configured, then accepted, then the request. The accepted URL is read from the store before the host
/// serves, changes with <see cref="KoanWebBaseUrl.Accept"/> while it runs, and is validated as a configured one is.
/// </summary>
public sealed class AcceptedBaseUrlSpec
{
    [Fact]
    public async Task With_nothing_configured_the_accepted_url_is_the_base_url_from_the_first_request()
    {
        var store = new MemoryApplicationUrlStore("https://place.example");
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(
            new Dictionary<string, string?>(), Probe(store), environment: "Production");
        using var client = host.Client;

        (await client.GetStringAsync(BaseUrlProbeController.Path)).Should().Be("https://place.example",
            "the accepted URL answers in Production, where a request-derived one is refused, whatever the request's Host");

        var baseUrl = host.Services.GetRequiredService<KoanWebBaseUrl>();
        baseUrl.Accepted.Should().Be("https://place.example");
        baseUrl.Current.Should().Be("https://place.example");
        baseUrl.IsConfigured.Should().BeFalse();

        var fact = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts
            .Should().ContainSingle(fact => fact.Code == KoanWebConstants.BaseUrl.FactCode).Subject;
        fact.Summary.Should().Contain("https://place.example").And.Contain(typeof(MemoryApplicationUrlStore).FullName!);
        fact.ReasonCode.Should().Be(KoanWebConstants.BaseUrl.AcceptedFactReason);
    }

    [Fact]
    public async Task The_canonical_web_application_reads_the_store_before_its_first_request()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Koan:Environment"] = "Production",
            ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
            ["Koan:BackgroundServices:Enabled"] = "false",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        Probe(new MemoryApplicationUrlStore("https://place.example"))(builder.Services);
        builder.Services.AddKoan();
        await using var app = builder.Build();
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var client = app.GetTestClient();

            (await client.GetStringAsync(BaseUrlProbeController.Path)).Should().Be("https://place.example");
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_configured_url_beats_an_accepted_one()
    {
        var store = new MemoryApplicationUrlStore("https://accepted.example");
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(
            new Dictionary<string, string?> { ["Koan:Web:ApplicationUrl"] = "https://configured.example" }, Probe(store));
        using var client = host.Client;
        var baseUrl = host.Services.GetRequiredService<KoanWebBaseUrl>();

        (await client.GetStringAsync(BaseUrlProbeController.Path)).Should().Be("https://configured.example");
        baseUrl.Accepted.Should().Be("https://accepted.example", "the accepted URL is still read, and stays shadowed");

        await baseUrl.Accept("https://later.example", TestContext.Current.CancellationToken);

        (await client.GetStringAsync(BaseUrlProbeController.Path)).Should().Be("https://configured.example");
        store.Value.Should().Be("https://later.example", "accepting still keeps the URL in the application's store");
    }

    [Fact]
    public async Task Accepting_while_running_changes_the_base_url_without_a_restart()
    {
        var store = new MemoryApplicationUrlStore(null);
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(
            new Dictionary<string, string?>(), Probe(store), environment: "Development");
        using var client = host.Client;
        var baseUrl = host.Services.GetRequiredService<KoanWebBaseUrl>();

        (await client.GetStringAsync(BaseUrlProbeController.Path)).Should().Be("http://localhost", "nothing is accepted yet");

        await baseUrl.Accept("HTTPS://Place.Example:443/", TestContext.Current.CancellationToken);

        (await client.GetStringAsync(BaseUrlProbeController.Path)).Should().Be("https://place.example");
        store.Value.Should().Be("https://place.example", "the store receives the URL normalised as it is held");
        store.Saves.Should().Be(1);
    }

    [Theory]
    [InlineData("place.example", "absolute http or https")]
    [InlineData("ftp://place.example", "absolute http or https")]
    [InlineData("https://place.example?tenant=a", "query")]
    [InlineData("https://user:secret@place.example", "user information")]
    [InlineData("http://0.0.0.0:5220", "unspecified listen address")]
    [InlineData("http://[::]:5220", "unspecified listen address")]
    [InlineData("https://place.example/tangent", "path")]
    public async Task An_accepted_url_is_validated_as_a_configured_one_is_and_is_an_origin(string candidate, string reason)
    {
        var store = new MemoryApplicationUrlStore(null);
        var baseUrl = new KoanWebBaseUrl(null, store: store);

        var accept = () => baseUrl.Accept(candidate, TestContext.Current.CancellationToken);

        (await accept.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain(reason).And.Contain("Koan:Web:ApplicationUrl");
        store.Saves.Should().Be(0, "nothing reaches the store unless it is a public origin");
        baseUrl.Accepted.Should().BeNull();
    }

    [Theory]
    [InlineData("Development", "http://127.0.0.1:5220")]
    [InlineData("Development", "http://localhost:5220")]
    [InlineData("Development", "https://tangent.example.com")]
    [InlineData("Test", "https://tangent.example.com")]
    [InlineData("Staging", "https://tangent.example.com")]
    [InlineData("Production", "https://tangent.example.com")]
    public async Task An_origin_sign_in_can_be_published_under_is_accepted(string environment, string candidate)
    {
        var store = new MemoryApplicationUrlStore(null);
        var baseUrl = new KoanWebBaseUrl(null, new EnvironmentStub(environment), store: store);

        await baseUrl.Accept(candidate, TestContext.Current.CancellationToken);

        baseUrl.Accepted.Should().Be(candidate);
        store.Value.Should().Be(candidate);
    }

    [Theory]
    [InlineData("Development", "http://tangent.lan:5220", "other than loopback")]
    [InlineData("Development", "http://192.168.1.10:5220", "other than loopback")]
    [InlineData("Test", "http://localhost:5220", "outside Development")]
    [InlineData("Production", "http://localhost:5220", "outside Development")]
    [InlineData("Production", "http://127.0.0.1:5220", "outside Development")]
    [InlineData("Production", "http://tangent.example.com", "other than loopback")]
    public async Task An_origin_that_would_break_sign_in_or_the_next_start_is_refused_before_the_store(
        string environment, string candidate, string reason)
    {
        var store = new MemoryApplicationUrlStore(null);
        var baseUrl = new KoanWebBaseUrl(null, new EnvironmentStub(environment), store: store);

        var accept = () => baseUrl.Accept(candidate, TestContext.Current.CancellationToken);

        (await accept.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain(reason).And.Contain("https");
        store.Saves.Should().Be(0);
        baseUrl.Accepted.Should().BeNull();
    }

    [Fact]
    public async Task Accepting_needs_the_application_store()
    {
        var baseUrl = new KoanWebBaseUrl(null);

        var accept = () => baseUrl.Accept("https://place.example", TestContext.Current.CancellationToken);

        (await accept.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(nameof(IApplicationUrlStore));
        baseUrl.Accepted.Should().BeNull();
    }

    private sealed class EnvironmentStub(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = nameof(AcceptedBaseUrlSpec);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    internal static Action<IServiceCollection> Probe(IApplicationUrlStore store) => services =>
    {
        services.AddSingleton(store);
        services.AddKoanControllersFrom<BaseUrlProbeController>();
    };
}

/// <summary>An application's accepted-URL store kept in memory, counting what it was given.</summary>
public sealed class MemoryApplicationUrlStore(string? value, bool derivesUntilAccepted = false) : IApplicationUrlStore
{
    private int _saves;

    public string? Value { get; private set; } = value;

    public bool DerivesUntilAccepted => derivesUntilAccepted;

    public int Saves => _saves;

    public Task<string?> Load(CancellationToken ct) => Task.FromResult(Value);

    public Task Save(string applicationUrl, CancellationToken ct)
    {
        Value = applicationUrl;
        Interlocked.Increment(ref _saves);
        return Task.CompletedTask;
    }
}

/// <summary>Answers the base URL Koan resolves for the request it receives.</summary>
[ApiController]
public sealed class BaseUrlProbeController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Path = "/base-url-probe";

    [HttpGet(Path)]
    public ContentResult Get() => Content(baseUrl.Resolve(HttpContext), "text/plain");
}
