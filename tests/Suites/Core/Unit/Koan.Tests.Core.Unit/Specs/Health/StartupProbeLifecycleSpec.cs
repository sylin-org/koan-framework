using Koan.Core;
using Koan.Core.Observability.Health;
using Koan.Core.Observability.Probes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Tests.Core.Unit.Specs.Health;

public sealed class StartupProbeLifecycleSpec
{
    [Fact]
    public async Task Stop_cancels_and_awaits_the_active_probe_before_the_next_contributor()
    {
        var active = new BlockingContributor("active");
        var next = new RecordingContributor("next");
        var aggregator = new HealthAggregator(new HealthAggregatorOptions());
        var service = new StartupProbeService(
            aggregator,
            new OrderedHealthRegistry(active, next),
            NullLogger<StartupProbeService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await active.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        var activeExitedAtStop = active.Exited.Task.IsCompleted;
        var nextCallsAtStop = next.CallCount;

        // Let the old fire-and-forget implementation finish before asserting so a red run cannot
        // leak work into another test.
        active.Release();
        if (!activeExitedAtStop)
        {
            await next.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        activeExitedAtStop.Should().BeTrue(
            "StopAsync must await the active contributor before host services can be disposed");
        nextCallsAtStop.Should().Be(0,
            "StopAsync must await the owned probe task before host services can be disposed");
    }

    [Fact]
    public async Task Targeted_probe_propagates_request_cancellation_to_the_contributor()
    {
        var contributor = new BlockingContributor("target");
        var aggregator = new HealthAggregator(new HealthAggregatorOptions());
        var bridge = new HealthContributorsBridge(
            new OrderedHealthRegistry(contributor),
            aggregator);

        await bridge.StartAsync(CancellationToken.None);
        using var requestCancellation = new CancellationTokenSource();
        var request = Task.Run(() => aggregator.RequestProbe(
            ProbeReason.Manual,
            contributor.Name,
            requestCancellation.Token));

        await contributor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        requestCancellation.Cancel();

        var completed = await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(2))) == request;
        var contributorExited = contributor.Exited.Task.IsCompleted;

        // Release the pre-fix handler so a red run leaves no blocked task.
        contributor.Release();
        await request.WaitAsync(TimeSpan.FromSeconds(2));
        await bridge.StopAsync(CancellationToken.None);

        completed.Should().BeTrue("the request token must bound the synchronous contributor dispatch");
        contributorExited.Should().BeTrue(
            "the contributor must exit from request cancellation before its manual release is signaled");
    }

    private sealed class OrderedHealthRegistry(params IHealthContributor[] contributors) : IHealthRegistry
    {
        private readonly List<IHealthContributor> _contributors = [.. contributors];

        public void Add(IHealthContributor contributor) => _contributors.Add(contributor);

        public IReadOnlyCollection<IHealthContributor> All => _contributors;
    }

    private sealed class BlockingContributor(string name) : IHealthContributor
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name { get; } = name;
        public bool IsCritical => true;
        public TaskCompletionSource Entered => _entered;
        public TaskCompletionSource Exited => _exited;

        public async Task<HealthReport> Check(CancellationToken ct = default)
        {
            _entered.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(ct);
                return new HealthReport(Name, HealthState.Healthy, "released", null, null);
            }
            finally
            {
                _exited.TrySetResult();
            }
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class RecordingContributor(string name) : IHealthContributor
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public string Name { get; } = name;
        public bool IsCritical => true;
        public int CallCount => Volatile.Read(ref _callCount);
        public TaskCompletionSource Entered => _entered;

        public Task<HealthReport> Check(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            _entered.TrySetResult();
            return Task.FromResult(new HealthReport(Name, HealthState.Healthy, "called", null, null));
        }
    }
}
