using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Singleflight;
using Xunit.Abstractions;

namespace Koan.Tests.Cache.Unit.Specs.Singleflight;

public sealed class CacheSingleflightRegistrySpec
{
    private readonly ITestOutputHelper _output;

    public CacheSingleflightRegistrySpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task RunAsync_enforces_exclusive_access_for_same_key()
        => SpecAsync(nameof(RunAsync_enforces_exclusive_access_for_same_key), async () =>
        {
            var registry = new CacheSingleflightRegistry();
            var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var first = registry.RunAsync(
                "shared",
                TimeSpan.FromSeconds(1),
                async ct =>
                {
                    firstEntered.TrySetResult(true);
                    await releaseFirst.Task.WaitAsync(ct);
                    return 1;
                },
                CancellationToken.None);

            await firstEntered.Task;

            var secondTask = Task.Run(async () =>
            {
                var value = await registry.RunAsync(
                    "shared",
                    TimeSpan.FromSeconds(1),
                    ct =>
                    {
                        secondStarted.TrySetResult(true);
                        return new ValueTask<int>(2);
                    },
                    CancellationToken.None).AsTask();

                return value;
            });

            await Task.Delay(20);
            secondStarted.Task.IsCompleted.Should().BeFalse("second caller should wait until the first releases the gate");

            releaseFirst.TrySetResult(true);

            await secondStarted.Task;
            var secondResult = await secondTask;
            secondResult.Should().Be(2);

            await first;
        });

    [Fact]
    public Task RunAsync_timeout_when_lock_not_acquired_within_window()
        => SpecAsync(nameof(RunAsync_timeout_when_lock_not_acquired_within_window), async () =>
        {
            var registry = new CacheSingleflightRegistry();
            var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var first = registry.RunAsync(
                "timeout",
                TimeSpan.FromSeconds(1),
                async ct =>
                {
                    firstEntered.TrySetResult(true);
                    await releaseFirst.Task.WaitAsync(ct);
                    return 1;
                },
                CancellationToken.None);

            await firstEntered.Task;

            var act = async () => await registry.RunAsync(
                "timeout",
                TimeSpan.FromMilliseconds(50),
                _ => new ValueTask<int>(0),
                CancellationToken.None).AsTask();

            await act.Should().ThrowAsync<TimeoutException>();

            releaseFirst.TrySetResult(true);
            await first;
        });

    private Task Spec(string scenario, Action body)
        => TestPipeline.For<CacheSingleflightRegistrySpec>(_output, scenario)
            .Assert(_ =>
            {
                body();
                return ValueTask.CompletedTask;
            })
            .RunAsync();

    private Task SpecAsync(string scenario, Func<Task> body)
        => TestPipeline.For<CacheSingleflightRegistrySpec>(_output, scenario)
            .Assert(async _ =>
            {
                await body().ConfigureAwait(false);
            })
            .RunAsync();
}
