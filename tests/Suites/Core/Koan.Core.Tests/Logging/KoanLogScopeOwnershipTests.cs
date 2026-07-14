using System.Collections.Concurrent;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Logging;
using Koan.Core.Tests.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Core.Tests.Logging;

[Collection(nameof(AppHostScopeTests))]
public sealed class KoanLogScopeOwnershipTests : IDisposable
{
    private const string Category = "Koan.Core.Tests.Logging.OwnershipProbe";
    private const string Action = "logging.ownership.probe";
    private const string StageEventName = "KoanStage";
    private const string StageActionProperty = "KoanStageAction";
    private const string StageOutcomeProperty = "KoanStageOutcome";

    private static readonly KoanLog.KoanLogScope Log = KoanLog.For(Category);

    private readonly IServiceProvider? _initialHost = AppHost.Current;

    public KoanLogScopeOwnershipTests()
    {
        AppHost.Current = null;
    }

    public void Dispose()
    {
        AppHost.Current = _initialHost;
    }

    [Fact]
    public void Static_scope_follows_newer_host_and_older_teardown_cannot_redirect_it()
    {
        var olderEntries = new ConcurrentQueue<string>();
        var newerEntries = new ConcurrentQueue<string>();
        using var olderProvider = CreateProvider(olderEntries);
        using var newerProvider = CreateProvider(newerEntries);

        using var olderLease = AppHost.Attach(olderProvider);
        Log.HostInfo(Action, "older");

        using var newerLease = AppHost.Attach(newerProvider);
        Log.HostInfo(Action, "newer");

        olderLease.Dispose();
        Log.HostInfo(Action, "after-older-stop");

        newerLease.Dispose();
        Log.HostInfo(Action, "hostless");

        olderEntries.Should().Equal("older");
        newerEntries.Should().Equal("newer", "after-older-stop");
        AppHost.Current.Should().BeNull();
    }

    [Fact]
    public async Task Static_scope_routes_concurrent_flows_to_their_selected_hosts()
    {
        var defaultEntries = new ConcurrentQueue<string>();
        var alphaEntries = new ConcurrentQueue<string>();
        var betaEntries = new ConcurrentQueue<string>();
        using var defaultProvider = CreateProvider(defaultEntries);
        using var alphaProvider = CreateProvider(alphaEntries);
        using var betaProvider = CreateProvider(betaEntries);
        using var defaultLease = AppHost.Attach(defaultProvider);

        async Task Emit(IServiceProvider provider, string outcome)
        {
            using (AppHost.PushScope(provider))
            {
                await Task.Yield();
                Log.HostInfo(Action, outcome);
            }
        }

        await Task.WhenAll(
            Emit(alphaProvider, "alpha"),
            Emit(betaProvider, "beta"));
        Log.HostInfo(Action, "default");

        alphaEntries.Should().Equal("alpha");
        betaEntries.Should().Equal("beta");
        defaultEntries.Should().Equal("default");
    }

    [Fact]
    public void AddKoanCore_registers_the_host_binder_before_other_hosted_services()
    {
        var services = new ServiceCollection();

        services.AddKoanCore();

        services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .First()
            .Should().Be(typeof(AppHostBinderHostedService));
    }

    private static ServiceProvider CreateProvider(ConcurrentQueue<string> entries)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new ProbeLoggerProvider(entries));
        });
        return services.BuildServiceProvider();
    }

    private sealed class ProbeLoggerProvider(ConcurrentQueue<string> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ProbeLogger(entries);
        public void Dispose() { }
    }

    private sealed class ProbeLogger(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name != StageEventName
                || state is not IEnumerable<KeyValuePair<string, object?>> properties)
            {
                return;
            }

            var values = properties.ToDictionary(pair => pair.Key, pair => pair.Value?.ToString());
            if (values.TryGetValue(StageActionProperty, out var action)
                && action == Action
                && values.TryGetValue(StageOutcomeProperty, out var outcome)
                && outcome is not null)
            {
                entries.Enqueue(outcome);
            }
        }
    }
}
