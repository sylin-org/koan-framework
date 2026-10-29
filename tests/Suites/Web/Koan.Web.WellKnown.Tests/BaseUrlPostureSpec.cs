using System.Net;
using AwesomeAssertions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// WEB-0078 / ARCH-0128: with <c>Koan:Web:ApplicationUrl</c> unset, the base URL derives from each request. That is
/// silent in Development, announced once in every other non-production environment, and refused in Production at
/// the point of need — the consumer that asks for an absolute URL fails with a correction naming the key, while the
/// host itself boots and serves.
/// </summary>
public sealed class BaseUrlPostureSpec
{
    [Fact]
    public async Task Production_boots_without_the_key_and_refuses_the_first_request_derived_origin()
    {
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(new Dictionary<string, string?>(), environment: "Production");
        using var client = host.Client;

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK, "an application that writes no absolute URL keeps running");

        var baseUrl = host.Services.GetRequiredService<KoanWebBaseUrl>();
        var resolve = () => baseUrl.Resolve(Request("https", "attacker.example", "/app"));

        resolve.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Production").And.Contain("Koan:Web:ApplicationUrl");
    }

    [Fact]
    public void A_non_production_environment_answers_from_the_request_and_says_so_once()
    {
        var log = new ListLogger();
        var baseUrl = new KoanWebBaseUrl(null, new EnvironmentStub("Test"), log);

        baseUrl.Resolve(Request("https", "app.example.test", "/app")).Should().Be("https://app.example.test/app");
        baseUrl.Resolve(Request("http", "localhost:5000", "")).Should().Be("http://localhost:5000");

        log.Warnings.Should().ContainSingle().Which.Should().Contain("Koan:Web:ApplicationUrl").And.Contain("Test");
    }

    [Fact]
    public void Development_answers_from_the_request_silently()
    {
        var log = new ListLogger();
        var baseUrl = new KoanWebBaseUrl(null, new EnvironmentStub("Development"), log);

        baseUrl.Resolve(Request("http", "localhost:5220", "")).Should().Be("http://localhost:5220");

        log.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Production_with_the_key_set_answers_the_configured_url()
    {
        var log = new ListLogger();
        var baseUrl = new KoanWebBaseUrl("https://www.example.com/app", new EnvironmentStub("Production"), log);

        baseUrl.Resolve(Request("http", "10.0.0.7:5080", "/app")).Should().Be("https://www.example.com/app");

        log.Warnings.Should().BeEmpty();
    }

    private static HttpRequest Request(string scheme, string host, string pathBase)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        context.Request.PathBase = pathBase;
        context.Request.Path = "/";
        return context.Request;
    }

    private sealed class EnvironmentStub(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "BaseUrlPostureSpec";
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
