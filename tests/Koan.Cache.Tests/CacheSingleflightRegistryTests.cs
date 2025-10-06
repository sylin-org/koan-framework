using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Cache.Singleflight;
using Xunit;

namespace Koan.Cache.Tests;

public sealed class CacheSingleflightRegistryTests
{
    [Fact]
    public async Task RunAsync_AllowsSingleExecution()
    {
        var registry = new CacheSingleflightRegistry();
        var concurrent = 0;
        var peak = 0;

        async ValueTask<int> Action(CancellationToken ct)
        {
            var now = Interlocked.Increment(ref concurrent);
            _ = Interlocked.Exchange(ref peak, Math.Max(peak, now));
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref concurrent);
            return 42;
        }

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => registry.RunAsync("key", TimeSpan.FromSeconds(1), Action, CancellationToken.None).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);
        results.Should().AllBeEquivalentTo(42);
        peak.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ThrowsTimeoutWhenSemaphoreUnavailable()
    {
        var registry = new CacheSingleflightRegistry();
        var tcs = new TaskCompletionSource();

        var first = registry.RunAsync("key", TimeSpan.FromSeconds(5), async ct =>
        {
            await tcs.Task.WaitAsync(ct);
            return 1;
        }, CancellationToken.None);

        await Task.Delay(20);

        Func<Task> act = async () => await registry.RunAsync("key", TimeSpan.FromMilliseconds(10), _ => ValueTask.FromResult(2), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<TimeoutException>();

        tcs.SetResult();
        await first;
    }
}
