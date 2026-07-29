using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Readiness;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Tests.Data.Core.Specs.Sources;

public sealed class SourceReadinessCoordinatorSpec
{
    [Fact]
    public async Task Concurrent_callers_share_one_stage_execution_and_success_is_memoized()
    {
        var coordinator = new DataSourceReadinessCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task Work(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task;
        }

        var first = coordinator.EnsureReachable(Plan(), "customers", Work);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = coordinator.EnsureReachable(Plan(), "customers", Work);
        release.TrySetResult();
        await Task.WhenAll(first, second);
        await coordinator.EnsureReachable(Plan(), "customers", Work);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task Caller_cancellation_detaches_without_cancelling_shared_work()
    {
        var coordinator = new DataSourceReadinessCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sharedToken = CancellationToken.None;

        async Task Work(CancellationToken token)
        {
            sharedToken = token;
            started.TrySetResult();
            await release.Task.WaitAsync(token);
        }

        var survivor = coordinator.ValidateShape(Plan(), "customers", Work);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var caller = new CancellationTokenSource();
        var detached = coordinator.ValidateShape(Plan(), "customers", Work, caller.Token);
        caller.Cancel();

        Func<Task> detachedAct = async () => await detached;
        await detachedAct.Should().ThrowAsync<OperationCanceledException>();
        survivor.IsCompleted.Should().BeFalse();
        sharedToken.IsCancellationRequested.Should().BeFalse();
        release.TrySetResult();
        await survivor;
    }

    [Fact]
    public async Task Failure_is_not_cached_and_next_call_can_recover()
    {
        var coordinator = new DataSourceReadinessCoordinator();
        var calls = 0;

        Task Work(CancellationToken _)
        {
            if (Interlocked.Increment(ref calls) == 1)
                return Task.FromException(new InvalidOperationException("unavailable"));
            return Task.CompletedTask;
        }

        Func<Task> firstAttempt = () => coordinator.ValidateShape(Plan(), "customers", Work);
        await firstAttempt.Should().ThrowAsync<InvalidOperationException>();
        await coordinator.ValidateShape(Plan(), "customers", Work);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task Readiness_shape_and_provisioning_have_distinct_state()
    {
        var coordinator = new DataSourceReadinessCoordinator();
        var calls = 0;
        Task Work(CancellationToken _) { Interlocked.Increment(ref calls); return Task.CompletedTask; }

        await coordinator.EnsureReachable(Plan(), "customers", Work);
        await coordinator.ValidateShape(Plan(), "customers", Work);
        await coordinator.Provision(Plan(), "customers", Work, Work);

        calls.Should().Be(4, "provisioning post-validates through the distinct shape stage");
    }

    [Fact]
    public async Task Concurrent_provisioning_runs_one_mutation_then_one_post_validation_and_memoizes_health()
    {
        var coordinator = new DataSourceReadinessCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provisionCalls = 0;
        var validationCalls = 0;

        async Task Provision(CancellationToken _)
        {
            Interlocked.Increment(ref provisionCalls);
            started.TrySetResult();
            await release.Task;
        }

        Task Validate(CancellationToken _)
        {
            Interlocked.Increment(ref validationCalls);
            return Task.CompletedTask;
        }

        var first = coordinator.Provision(Plan(), "customers", Provision, Validate);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = coordinator.Provision(Plan(), "customers", Provision, Validate);
        release.TrySetResult();
        await Task.WhenAll(first, second);
        await coordinator.Provision(Plan(), "customers", Provision, Validate);

        provisionCalls.Should().Be(1);
        validationCalls.Should().Be(1);
    }

    [Fact]
    public async Task Provisioning_policy_rejects_before_work_is_created()
    {
        var coordinator = new DataSourceReadinessCoordinator();
        var calls = 0;
        var external = Plan(StorageLifecycle.External, DataSourceAccess.ReadWrite);

        var act = () => coordinator.Provision(
            external,
            "customers",
            _ => { calls++; return Task.CompletedTask; },
            _ => { calls++; return Task.CompletedTask; });

        await act.Should().ThrowAsync<DataSourcePolicyException>();
        calls.Should().Be(0);
    }

    [Fact]
    public async Task Coordinators_are_host_isolated()
    {
        var calls = 0;
        Task Work(CancellationToken _) { Interlocked.Increment(ref calls); return Task.CompletedTask; }

        await new DataSourceReadinessCoordinator().EnsureReachable(Plan(), "customers", Work);
        await new DataSourceReadinessCoordinator().EnsureReachable(Plan(), "customers", Work);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task Healthy_stage_cache_is_bounded_per_host()
    {
        var coordinator = new DataSourceReadinessCoordinator(
            options: Options.Create(new DataRuntimeOptions { ReadinessCacheEntries = 1 }));
        var calls = 0;
        Task Work(CancellationToken _) { calls++; return Task.CompletedTask; }

        await coordinator.EnsureReachable(Plan(), "first", Work);
        await coordinator.EnsureReachable(Plan(), "second", Work);
        await coordinator.EnsureReachable(Plan(), "first", Work);

        calls.Should().Be(3);
    }

    [Fact]
    public async Task Host_shutdown_owns_shared_work_cancellation()
    {
        using var lifetime = new TestLifetime();
        var coordinator = new DataSourceReadinessCoordinator(lifetime);
        var observed = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);

        var work = coordinator.EnsureReachable(Plan(), "customers", async token =>
        {
            observed.TrySetResult(token);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        var token = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lifetime.Stop();

        Func<Task> shutdownAct = async () => await work;
        await shutdownAct.Should().ThrowAsync<OperationCanceledException>();
        token.IsCancellationRequested.Should().BeTrue();
    }

    private static DataSourcePlan Plan(
        StorageLifecycle lifecycle = StorageLifecycle.Managed,
        DataSourceAccess access = DataSourceAccess.ReadWrite) =>
        new("Default", "spy", lifecycle, access, "route", "connection");

    private sealed class TestLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;
        public void StopApplication() => Stop();
        public void Stop() => _stopping.Cancel();
        public void Dispose()
        {
            _started.Dispose();
            _stopping.Dispose();
            _stopped.Dispose();
        }
    }
}
