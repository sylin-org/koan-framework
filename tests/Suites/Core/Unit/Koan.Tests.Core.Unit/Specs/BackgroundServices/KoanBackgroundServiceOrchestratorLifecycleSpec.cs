using System.Collections.Concurrent;
using Koan.Core.BackgroundServices;
using Koan.Tests.Core.Unit.Specs.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Tests.Core.Unit.Specs.BackgroundServices;

[Collection(HealthProbeSchedulerOwnershipCollection.Name)]
public sealed class KoanBackgroundServiceOrchestratorLifecycleSpec
{
    private static readonly TimeSpan AssertionTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EarlyCompletionWindow = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task Stop_awaits_owned_child_cleanup_before_returning()
    {
        var child = new CleanupBlockingService<int>();
        using var provider = CreateProvider(child);
        using var orchestrator = CreateOrchestrator(provider);

        await orchestrator.StartAsync(CancellationToken.None);
        await child.Entered.Task.WaitAsync(AssertionTimeout);

        using var shutdown = new CancellationTokenSource(AssertionTimeout);
        var stop = orchestrator.StopAsync(shutdown.Token);
        await child.CancellationObserved.Task.WaitAsync(AssertionTimeout);
        var returnedBeforeCleanup = await Task.WhenAny(stop, Task.Delay(EarlyCompletionWindow)) == stop;

        child.ReleaseCleanup();
        await stop.WaitAsync(AssertionTimeout);
        await child.Exited.Task.WaitAsync(AssertionTimeout);

        returnedBeforeCleanup.Should().BeFalse(
            "the orchestrator owns the child task until cancellation-aware cleanup completes");
    }

    [Fact]
    public async Task Stop_remains_bounded_by_the_host_shutdown_token()
    {
        var child = new CleanupBlockingService<int>();
        using var provider = CreateProvider(child);
        using var orchestrator = CreateOrchestrator(provider);

        await orchestrator.StartAsync(CancellationToken.None);
        await child.Entered.Task.WaitAsync(AssertionTimeout);

        using var shutdown = new CancellationTokenSource(EarlyCompletionWindow);
        await orchestrator.StopAsync(shutdown.Token).WaitAsync(AssertionTimeout);
        var exitedAtDeadline = child.Exited.Task.IsCompleted;

        child.ReleaseCleanup();
        await child.Exited.Task.WaitAsync(AssertionTimeout);

        exitedAtDeadline.Should().BeFalse(
            "a non-cooperative child may outlive graceful shutdown once the host deadline expires");
    }

    [Fact]
    public async Task Stop_logs_a_child_fault_completed_during_graceful_shutdown()
    {
        var child = new FaultOnCancellationService<string>();
        var logger = new RecordingLogger<KoanBackgroundServiceOrchestrator>();
        using var provider = CreateProvider(child);
        using var orchestrator = CreateOrchestrator(provider, logger);

        await orchestrator.StartAsync(CancellationToken.None);
        await child.Entered.Task.WaitAsync(AssertionTimeout);

        using var shutdown = new CancellationTokenSource(AssertionTimeout);
        await orchestrator.StopAsync(shutdown.Token).WaitAsync(AssertionTimeout);
        await child.Exited.Task.WaitAsync(AssertionTimeout);

        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains(child.Name, StringComparison.Ordinal)
            && entry.Exception != null
            && entry.Exception.ToString().Contains(FaultOnCancellationService<string>.FailureMessage, StringComparison.Ordinal));
    }

    private static ServiceProvider CreateProvider(IKoanBackgroundService child)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKoanBackgroundService>(child);
        services.AddOptions();
        return services.BuildServiceProvider();
    }

    private static KoanBackgroundServiceOrchestrator CreateOrchestrator(
        ServiceProvider provider,
        ILogger<KoanBackgroundServiceOrchestrator>? logger = null)
        => new(
            provider,
            logger ?? new RecordingLogger<KoanBackgroundServiceOrchestrator>(),
            provider.GetRequiredService<IOptionsMonitor<KoanBackgroundServiceOptions>>());

    private sealed class CleanupBlockingService<TMarker> : IKoanBackgroundService
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseCleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => typeof(CleanupBlockingService<TMarker>).Name;
        public TaskCompletionSource Entered => _entered;
        public TaskCompletionSource CancellationObserved => _cancellationObserved;
        public TaskCompletionSource Exited => _exited;

        public async Task Execute(CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _cancellationObserved.TrySetResult();
                await _releaseCleanup.Task;
            }
            finally
            {
                _exited.TrySetResult();
            }
        }

        public void ReleaseCleanup() => _releaseCleanup.TrySetResult();
    }

    private sealed class FaultOnCancellationService<TMarker> : IKoanBackgroundService
    {
        public const string FailureMessage = "child cleanup failed";

        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => typeof(FaultOnCancellationService<TMarker>).Name;
        public TaskCompletionSource Entered => _entered;
        public TaskCompletionSource Exited => _exited;

        public async Task Execute(CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(FailureMessage);
            }
            finally
            {
                _exited.TrySetResult();
            }
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
