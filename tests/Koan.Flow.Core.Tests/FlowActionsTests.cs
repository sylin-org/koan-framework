using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Data.Core;
using Koan.Flow;
using Koan.Flow.Actions;
using Koan.Flow.Infrastructure;
using Koan.Flow.Model;
using Koan.Messaging;
using Xunit;

namespace Koan.Flow.Core.Tests;

public class FlowActionsTests
{
    private static IHost? _host;

    private static async Task<(IServiceProvider sp, FakeBus bus)> CreateHostAsync(string workDir)
    {
        if (_host is not null)
        {
            try { Koan.Core.Hosting.App.AppHost.Current = null; } catch { }
            try { await _host.StopAsync(TimeSpan.FromSeconds(1)); } catch { }
            try { _host.Dispose(); } catch { }
            _host = null;
        }
        Koan.Data.Core.TestHooks.ResetDataConfigs();
        var cfgDict = new Dictionary<string, string?>
        {
            { "Koan_DATA_PROVIDER", "json" },
            { "Koan:Data:Json:DirectoryPath", workDir },
        };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(cfgDict!).Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddKoanDataCore();
        sc.AddKoanFlow();
        sc.AddMessagingCore();
        // Override messaging selector to use a fake in-memory bus
        var fakeBus = new FakeBus();
        sc.AddSingleton(fakeBus);
        sc.AddSingleton<IMessageBusSelector>(sp => new FakeSelector(sp.GetRequiredService<FakeBus>()));

        // Host and start
        var host = new HostBuilder().ConfigureServices(s => { foreach (var sd in sc) s.Add(sd); }).Build();
        await host.StartAsync();
        Koan.Core.Hosting.App.AppHost.Current = host.Services;
        _host = host;
        return (host.Services, fakeBus);
    }

    [Fact]
    public async Task FlowActions_Seed_Enqueues_Intake_And_Acks()
    {
    var dir = NewWorkDir();
    using var _ = new TempDir(dir);
        var (sp, bus) = await CreateHostAsync(dir);
        var actions = sp.GetRequiredService<IFlowActions>();

        var model = FlowRegistry.GetModelName(typeof(TestModel));
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["dummy"] = "a1",
            ["name.first"] = "Ann"
        };

    await actions.SeedAsync(model, referenceId: "ref-seed-1", payload: payload, ct: CancellationToken.None);

        // Expect an ok ack
        await bus.WaitUntilAsync(() => bus.Sent.OfType<FlowAck>().Any(a => a.Model == model && a.Status == "ok"), TimeSpan.FromSeconds(5));
        bus.Sent.OfType<FlowAck>().Should().Contain(a => a.Model == model && a.Status == "ok");

        // Verify a record exists in intake
        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
        {
            var page = await Data<StageRecord<TestModel>, string>.FirstPage(10, CancellationToken.None);
            page.Any(r => TryGetStageValue(r.Data, "dummy") == "a1").Should().BeTrue();
        }
    }

    [Fact]
    public async Task FlowActions_Report_Emits_Stats()
    {
    var dir = NewWorkDir();
    using var _ = new TempDir(dir);
        var (sp, bus) = await CreateHostAsync(dir);
        var actions = sp.GetRequiredService<IFlowActions>();

        var model = FlowRegistry.GetModelName(typeof(TestModel));
    await actions.ReportAsync(model, referenceId: "ref-report-1", payload: new { }, ct: CancellationToken.None);

        await bus.WaitUntilAsync(() => bus.Sent.OfType<FlowReport>().Any(r => r.Model == model), TimeSpan.FromSeconds(5));
        var report = bus.Sent.OfType<FlowReport>().Last(r => r.Model == model);
    var stats = report.Stats as IDictionary<string, object> ?? report.Stats as IDictionary<string, object?>;
    stats.Should().NotBeNull();
    stats!.Keys.Should().Contain(new[] { "intake", "standardized", "keyed", "canonical", "lineage", "roots", "policies" });
    }

    [Fact]
    public async Task FlowActions_Ping_Acks()
    {
    var dir = NewWorkDir();
    using var _ = new TempDir(dir);
        var (sp, bus) = await CreateHostAsync(dir);
        var actions = sp.GetRequiredService<IFlowActions>();

        var model = FlowRegistry.GetModelName(typeof(TestModel));
        await actions.PingAsync(model, ct: CancellationToken.None);

        await bus.WaitUntilAsync(() => bus.Sent.OfType<FlowAck>().Any(a => a.Model == model && a.Status == "ok"), TimeSpan.FromSeconds(5));
        bus.Sent.OfType<FlowAck>().Should().Contain(a => a.Model == model && a.Status == "ok");
    }

    // Fake bus that dispatches FlowAction to the registered handler and captures outgoing messages
    public sealed class FakeBus : IMessageBus
    {
        private readonly ConcurrentQueue<object> _sent = new();
        public IReadOnlyCollection<object> Sent => _sent.ToArray();

        public Task SendAsync(object message, CancellationToken ct = default)
        {
            if (message is FlowAction fa)
            {
                var sp = Koan.Core.Hosting.App.AppHost.Current ?? throw new InvalidOperationException("AppHost.Current not set");
                var handler = sp.GetRequiredService<IMessageHandler<FlowAction>>();
                var env = new MessageEnvelope(Guid.NewGuid().ToString("n"), typeof(FlowAction).FullName!, fa.CorrelationId, null, new Dictionary<string, string>(), 1, DateTimeOffset.UtcNow);
                return handler.HandleAsync(env, fa, ct);
            }
            _sent.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task SendManyAsync(IEnumerable<object> messages, CancellationToken ct = default)
        {
            var tasks = messages.Select(m => SendAsync(m, ct));
            return Task.WhenAll(tasks);
        }

        // Helper to await a condition on captured messages
        public async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout, TimeSpan? poll = null)
        {
            var start = DateTimeOffset.UtcNow;
            var interval = poll ?? TimeSpan.FromMilliseconds(100);
            while (DateTimeOffset.UtcNow - start < timeout)
            {
                if (predicate()) return;
                await Task.Delay(interval);
            }
            predicate();
        }
    }

    private sealed class FakeSelector : IMessageBusSelector
    {
        private readonly FakeBus _bus;
        public FakeSelector(FakeBus bus) => _bus = bus;
        public IMessageBus ResolveDefault(IServiceProvider sp) => _bus;
        public IMessageBus Resolve(IServiceProvider sp, string busCode) => _bus;
    }

    // Local helpers (avoid reaching into other test classes)
    private static string NewWorkDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Koan-flow-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _dir;
        public TempDir(string dir) { _dir = dir; }
        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        }
    }

    private static string? TryGetStageValue(object? stagePayload, string key)
    {
        if (stagePayload is IDictionary<string, object?> d)
        {
            return d.TryGetValue(key, out var v) ? v?.ToString() : null;
        }
        if (stagePayload is IDictionary<string, object> d2)
        {
            return d2.TryGetValue(key, out var v) ? v?.ToString() : null;
        }
        return null;
    }
}
