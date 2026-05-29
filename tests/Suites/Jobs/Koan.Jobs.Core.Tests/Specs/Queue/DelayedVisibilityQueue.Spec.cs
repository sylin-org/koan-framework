using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Core.Tests.Support;
using Koan.Jobs.Model;
using Koan.Jobs.Queue;

namespace Koan.Jobs.Core.Tests.Specs.Queue;

/// <summary>
/// Behaviour specs for the time-aware in-memory queue (JOBS-0002 delayed-visibility): immediately
/// visible items dispatch at once; future-visible items are withheld until their due time; an
/// immediate item is dispatchable ahead of an already-queued delayed one.
/// </summary>
public sealed class DelayedVisibilityQueueSpec
{
    private static JobQueueItem Item(string id) => new(id, typeof(StubJob), Lane: null);

    private static async Task<JobQueueItem?> ReadOne(IJobQueue queue, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (var item in queue.ReadAll(cts.Token))
            {
                return item;
            }
        }
        catch (OperationCanceledException)
        {
        }
        return null;
    }

    [Fact]
    public async Task Immediate_enqueue_is_readable_at_once()
    {
        using var queue = new InMemoryJobQueue();
        await queue.Enqueue(Item("a"), CancellationToken.None);

        var read = await ReadOne(queue, TimeSpan.FromMilliseconds(500));
        read.Should().NotBeNull();
        read!.Value.JobId.Should().Be("a");
    }

    [Fact]
    public async Task Future_visible_item_is_withheld_until_due_then_released()
    {
        using var queue = new InMemoryJobQueue();
        await queue.Enqueue(Item("later"), DateTimeOffset.UtcNow.AddMilliseconds(400), CancellationToken.None);

        // Well before the due time — nothing should be readable.
        var early = await ReadOne(queue, TimeSpan.FromMilliseconds(150));
        early.Should().BeNull();

        // Past the due time — the item is now dispatchable.
        var late = await ReadOne(queue, TimeSpan.FromMilliseconds(800));
        late.Should().NotBeNull();
        late!.Value.JobId.Should().Be("later");
    }

    [Fact]
    public async Task Immediate_item_is_dispatchable_ahead_of_an_already_queued_delayed_item()
    {
        using var queue = new InMemoryJobQueue();
        await queue.Enqueue(Item("delayed"), DateTimeOffset.UtcNow.AddMilliseconds(500), CancellationToken.None);
        await queue.Enqueue(Item("now"), CancellationToken.None);

        var first = await ReadOne(queue, TimeSpan.FromMilliseconds(200));
        first.Should().NotBeNull();
        first!.Value.JobId.Should().Be("now");
    }

    [Fact]
    public async Task Past_visible_at_is_treated_as_immediate()
    {
        using var queue = new InMemoryJobQueue();
        await queue.Enqueue(Item("past"), DateTimeOffset.UtcNow.AddSeconds(-5), CancellationToken.None);

        var read = await ReadOne(queue, TimeSpan.FromMilliseconds(500));
        read.Should().NotBeNull();
        read!.Value.JobId.Should().Be("past");
    }
}
