using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Concurrency;
using Xunit;

namespace Koan.Core.Tests;

public sealed class KeyedLeaseGateSpec
{
    [Fact]
    public async Task RunAsync_enforces_exclusive_access_for_same_key()
    {
        var registry = new KeyedLeaseGate();
        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = registry.RunAsync<int>(
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
            return await registry.RunAsync<int>(
                "shared",
                TimeSpan.FromSeconds(1),
                ct =>
                {
                    secondStarted.TrySetResult(true);
                    return new ValueTask<int>(2);
                },
                CancellationToken.None);
        });

        await Task.Delay(20);
        secondStarted.Task.IsCompleted.Should().BeFalse(
            "second caller should wait until the first releases the gate");

        releaseFirst.TrySetResult(true);

        await secondStarted.Task;
        var secondResult = await secondTask;
        secondResult.Should().Be(2);

        await first;
    }

    [Fact]
    public async Task RunAsync_timeout_when_lock_not_acquired_within_window()
    {
        var registry = new KeyedLeaseGate();
        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = registry.RunAsync<int>(
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

        var act = async () => await registry.RunAsync<int>(
            "timeout",
            TimeSpan.FromMilliseconds(50),
            _ => new ValueTask<int>(0),
            CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();

        releaseFirst.TrySetResult(true);
        await first;
    }

    [Fact]
    public async Task RunAsync_distinct_keys_run_concurrently()
    {
        var registry = new KeyedLeaseGate();
        var aEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var a = registry.RunAsync<int>("A", TimeSpan.FromSeconds(1), async ct =>
        {
            aEntered.TrySetResult(true);
            await release.Task.WaitAsync(ct);
            return 1;
        }, CancellationToken.None);

        var b = registry.RunAsync<int>("B", TimeSpan.FromSeconds(1), async ct =>
        {
            bEntered.TrySetResult(true);
            await release.Task.WaitAsync(ct);
            return 2;
        }, CancellationToken.None);

        await aEntered.Task;
        await bEntered.Task;  // Both must enter before either releases — proves they don't share a gate.

        release.TrySetResult(true);
        (await a).Should().Be(1);
        (await b).Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_releases_gate_when_action_throws()
    {
        var registry = new KeyedLeaseGate();

        var bang = async () => await registry.RunAsync<int>(
            "boom",
            TimeSpan.FromSeconds(1),
            _ => throw new InvalidOperationException("intentional"),
            CancellationToken.None);

        await bang.Should().ThrowAsync<InvalidOperationException>();

        // Next call must succeed — gate must have been released even though action threw.
        var ok = await registry.RunAsync<int>(
            "boom",
            TimeSpan.FromSeconds(1),
            _ => new ValueTask<int>(42),
            CancellationToken.None);

        ok.Should().Be(42);
    }
}
