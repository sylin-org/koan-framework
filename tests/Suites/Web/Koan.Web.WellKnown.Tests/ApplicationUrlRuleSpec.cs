using System.Collections.Concurrent;
using AwesomeAssertions;
using Koan.Web.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// WEB-0081: a capability active in the host says what it cannot use. <see cref="KoanWebBaseUrl.Accept"/> refuses that
/// before the store sees it, a stored one is not held, and an application can ask first. With no rule, an https origin
/// on any port is an origin like another.
/// </summary>
public sealed class ApplicationUrlRuleSpec
{
    private const string Refused = "https://place.example:8443";
    private const string Usable = "https://place.example";

    [Fact]
    public async Task With_no_rule_an_https_origin_on_another_port_is_accepted()
    {
        var store = new MemoryApplicationUrlStore(null);
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(new Dictionary<string, string?>(), AcceptedBaseUrlSpec.Probe(store));
        var baseUrl = host.Services.GetRequiredService<KoanWebBaseUrl>();

        await baseUrl.Accept(Refused, TestContext.Current.CancellationToken);

        baseUrl.Accepted.Should().Be(Refused);
        store.Value.Should().Be(Refused);
    }

    [Fact]
    public async Task An_origin_a_rule_refuses_never_reaches_the_store_and_the_reason_is_named()
    {
        var store = new MemoryApplicationUrlStore(Usable);
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(new Dictionary<string, string?>(), WithRule(store));
        var baseUrl = host.Services.GetRequiredService<KoanWebBaseUrl>();

        var accept = () => baseUrl.Accept(Refused, TestContext.Current.CancellationToken);

        (await accept.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain(Refused).And.Contain(PortRule.Reason);
        store.Saves.Should().Be(0);
        baseUrl.Accepted.Should().Be(Usable);
    }

    [Fact]
    public async Task An_application_can_ask_first()
    {
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(new Dictionary<string, string?>(), WithRule(new MemoryApplicationUrlStore(null)));
        var baseUrl = host.Services.GetRequiredService<KoanWebBaseUrl>();

        baseUrl.WouldAccept(Usable, out var none).Should().BeTrue();
        none.Should().BeEmpty();
        baseUrl.WouldAccept(Refused, out var problem).Should().BeFalse();
        problem.Should().Be(PortRule.Reason);
        baseUrl.WouldAccept("https://place.example/app", out problem).Should().BeFalse();
        problem.Should().Contain("path");
    }

    [Fact]
    public void Without_a_store_nothing_would_be_accepted()
    {
        new KoanWebBaseUrl(null).WouldAccept(Usable, out var problem).Should().BeFalse();
        problem.Should().Contain(nameof(IApplicationUrlStore));
    }

    [Fact]
    public async Task A_stored_origin_a_rule_refuses_is_set_aside_with_one_warning_and_the_store_is_left_alone()
    {
        var store = new MemoryApplicationUrlStore(Refused, derivesUntilAccepted: true);
        var log = new WarningLog();
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(new Dictionary<string, string?>(), services =>
        {
            WithRule(store)(services);
            services.AddSingleton<ILoggerProvider>(log);
        }, environment: "Production");
        var baseUrl = host.Services.GetRequiredService<KoanWebBaseUrl>();

        baseUrl.Accepted.Should().BeNull();
        baseUrl.DerivesFromRequests.Should().BeTrue();
        log.Messages.Where(message => message.Contains("Koan does not use it")).Should().ContainSingle()
            .Which.Should().Contain(Refused).And.Contain(PortRule.Reason);
        store.Saves.Should().Be(0);
        store.Value.Should().Be(Refused);
    }

    private static Action<IServiceCollection> WithRule(IApplicationUrlStore store) => services =>
    {
        AcceptedBaseUrlSpec.Probe(store)(services);
        services.AddSingleton<IApplicationUrlRule, PortRule>();
    };

    private sealed class PortRule : IApplicationUrlRule
    {
        public const string Reason = "it is on a port of its own";

        public string? Refuses(string origin) => new Uri(origin).IsDefaultPort ? null : Reason;
    }

    private sealed class WarningLog : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IEnumerable<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new Logger(_messages);

        public void Dispose() { }

        private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning) messages.Enqueue(formatter(state, exception));
            }
        }
    }
}
