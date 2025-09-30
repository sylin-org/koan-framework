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
using Koan.Canon;
using Koan.Canon.Actions;
using Koan.Canon.Infrastructure;
using Koan.Canon.Model;
using Koan.Messaging;
using Xunit;

namespace Koan.Canon.Core.Tests;

public class CanonActionsTests
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
    sc.AddKoanCanon();
    sc.AddKoanMessaging();
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
    public async Task CanonActions_Seed_Enqueues_Intake_And_Acks()
    {
    var dir = NewWorkDir();
    using var _ = new TempDir(dir);
        var (sp, bus) = await CreateHostAsync(dir);
        var actions = sp.GetRequiredService<ICanonActions>();

        var model = CanonRegistry.GetModelName(typeof(TestModel));
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["dummy"] = "a1",
            ["name.first"] = "Ann"
        };

    await actions.SeedAsync(model, referenceId: "ref-seed-1", payload: payload, ct: CancellationToken.None);

        // Expect an ok ack
        await bus.WaitUntilAsync(() => bus.Sent.OfType<CanonAck>().Any(a => a.Model == model && a.Status == "ok"), TimeSpan.FromSeconds(5));
        bus.Sent.OfType<CanonAck>().Should().Contain(a => a.Model == model && a.Status == "ok");

        // Verify a record exists in intake
        using (DataSetContext.With(CanonSets.StageShort(CanonSets.Intake)))
        {
            var page = await Data<StageRecord<TestModel>, string>.FirstPage(10, CancellationToken.None);
            page.Any(r => TryGetStageValue(r.Data, "dummy") == "a1").Should().BeTrue();
        }
    }

    [Fact]
    public async Task CanonActions_Report_Emits_Stats()
    {
    var dir = NewWorkDir();
    using var _ = new TempDir(dir);
        var (sp, bus) = await CreateHostAsync(dir);
        var actions = sp.GetRequiredService<ICanonActions>();

        var model = CanonRegistry.GetModelName(typeof(TestModel));
    await actions.ReportAsync(model, referenceId: "ref-report-1", payload: new { }, ct: CancellationToken.None);

        await bus.WaitUntilAsync(() => bus.Sent.OfType<CanonReport>().Any(r => r.Model == model), TimeSpan.FromSeconds(5));
        var report = bus.Sent.OfType<CanonReport>().Last(r => r.Model == model);
    var stats = report.Stats as IDictionary<string, object?>;
    stats.Should().NotBeNull();
    stats!.Keys.Should().Contain(new[] { "intake", "standardized", "keyed", "Canonical", "lineage", "roots", "policies" });
    }

    [Fact]
    public async Task CanonActions_Ping_Acks()
    {
    var dir = NewWorkDir();
    using var _ = new TempDir(dir);
        var (sp, bus) = await CreateHostAsync(dir);
        var actions = sp.GetRequiredService<ICanonActions>();

        var model = CanonRegistry.GetModelName(typeof(TestModel));
        await actions.PingAsync(model, ct: CancellationToken.None);

        await bus.WaitUntilAsync(() => bus.Sent.OfType<CanonAck>().Any(a => a.Model == model && a.Status == "ok"), TimeSpan.FromSeconds(5));
        bus.Sent.OfType<CanonAck>().Should().Contain(a => a.Model == model && a.Status == "ok");
    }

    // Fake bus that captures outgoing messages and replays consumers the way the modern messaging stack expects
    public sealed class FakeBus : IMessageBus
    {
        private readonly ConcurrentQueue<object> _sent = new();
        private readonly ConcurrentDictionary<Type, List<FakeConsumer>> _consumers = new();
        private readonly ConcurrentDictionary<Type, ConcurrentQueue<object>> _pending = new();

        public IReadOnlyCollection<object> Sent => _sent.ToArray();

        public Task SendAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            if (message is null) throw new ArgumentNullException(nameof(message));

            if (TryDispatch(typeof(T), message, out var dispatchTask))
            {
                return dispatchTask;
            }

            _pending.GetOrAdd(typeof(T), _ => new ConcurrentQueue<object>()).Enqueue(message);

            if (message is not CanonAction)
            {
                _sent.Enqueue(message);
            }

            return Task.CompletedTask;
        }

        public async Task<IMessageConsumer> CreateConsumerAsync<T>(Func<T, Task> handler, CancellationToken cancellationToken = default) where T : class
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            cancellationToken.ThrowIfCancellationRequested();

            var messageType = typeof(T);
            var consumer = new FakeConsumer(messageType, obj => handler((T)obj), this);

            var list = _consumers.GetOrAdd(messageType, _ => new List<FakeConsumer>());
            lock (list)
            {
                list.Add(consumer);
            }

            if (_pending.TryRemove(messageType, out var queue))
            {
                while (queue.TryDequeue(out var pending))
                {
                    if (TryDispatch(messageType, pending, out var replayTask))
                    {
                        await replayTask.ConfigureAwait(false);
                    }
                    else
                    {
                        // No active consumer (all paused). Requeue for later delivery.
                        _pending.GetOrAdd(messageType, _ => new ConcurrentQueue<object>()).Enqueue(pending);
                    }
                }
            }

            return consumer;
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

    private void RemoveConsumer(FakeConsumer consumer)
        {
            if (_consumers.TryGetValue(consumer.MessageType, out var list))
            {
                lock (list)
                {
                    list.Remove(consumer);
                    if (list.Count == 0)
                    {
                        _consumers.TryRemove(consumer.MessageType, out _);
                    }
                }
            }
        }

        private bool TryDispatch(Type messageType, object message, out Task dispatchTask)
        {
            if (_consumers.TryGetValue(messageType, out var list))
            {
                FakeConsumer[] snapshot;
                lock (list)
                {
                    snapshot = list.ToArray();
                }

                var tasks = snapshot.Where(c => c.IsActive).Select(c => c.ProcessAsync(message)).ToList();
                if (tasks.Count > 0)
                {
                    dispatchTask = Task.WhenAll(tasks);
                    return true;
                }
            }

            dispatchTask = Task.CompletedTask;
            return false;
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

        private sealed class FakeConsumer : IMessageConsumer
        {
            private readonly Func<object, Task> _handler;
            private readonly FakeBus _bus;
            private volatile bool _active = true;

            public FakeConsumer(Type messageType, Func<object, Task> handler, FakeBus bus)
            {
                MessageType = messageType;
                Destination = messageType.FullName ?? messageType.Name;
                _handler = handler;
                _bus = bus;
            }

            public Type MessageType { get; }
            public string Destination { get; }
            public bool IsActive => _active;

            public Task PauseAsync()
            {
                _active = false;
                return Task.CompletedTask;
            }

            public Task ResumeAsync()
            {
                _active = true;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                _active = false;
                _bus.RemoveConsumer(this);
                return ValueTask.CompletedTask;
            }

            public Task ProcessAsync(object message)
            {
                if (!_active)
                {
                    return Task.CompletedTask;
                }

                return _handler(message);
            }
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
        var dir = Path.Combine(Path.GetTempPath(), "Koan-Canon-tests", Guid.NewGuid().ToString("n"));
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



