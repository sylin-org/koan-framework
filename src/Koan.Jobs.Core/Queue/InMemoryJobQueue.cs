using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Koan.Jobs.Queue;

/// <summary>
/// Time-aware in-memory dispatch queue (JOBS-0002). Immediately-visible items flow straight into a
/// ready <see cref="Channel{T}"/>; future-visible items wait in a due-time min-heap that a single
/// pump loop drains into the ready channel as they come due. This makes deferral
/// (retry/rate-gate/dependency-block backoff) a queue concern — the executor re-queues with a
/// <c>visibleAt</c> and returns immediately rather than sleeping while holding a lane permit — and
/// gives scheduled/delayed jobs for free.
/// </summary>
internal sealed class InMemoryJobQueue : IJobQueue, IDisposable
{
    private readonly Channel<JobQueueItem> _ready;
    private readonly PriorityQueue<JobQueueItem, DateTimeOffset> _scheduled = new();
    private readonly object _scheduleLock = new();
    private readonly SemaphoreSlim _wakeup = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;

    public InMemoryJobQueue()
    {
        _ready = Channel.CreateUnbounded<JobQueueItem>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false
        });
        _pump = Task.Run(() => PumpAsync(_cts.Token));
    }

    public ValueTask Enqueue(JobQueueItem item, CancellationToken cancellationToken)
        => Enqueue(item, DateTimeOffset.UtcNow, cancellationToken);

    public ValueTask Enqueue(JobQueueItem item, DateTimeOffset visibleAt, CancellationToken cancellationToken)
    {
        if (visibleAt <= DateTimeOffset.UtcNow)
        {
            // Unbounded channel — write always succeeds synchronously.
            _ready.Writer.TryWrite(item);
            return ValueTask.CompletedTask;
        }

        lock (_scheduleLock)
        {
            _scheduled.Enqueue(item, visibleAt);
        }
        // Nudge the pump so it recomputes its sleep against the (possibly earlier) new due time.
        _wakeup.Release();
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<JobQueueItem> ReadAll([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await _ready.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_ready.Reader.TryRead(out var item))
            {
                yield return item;
            }
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan wait;
            lock (_scheduleLock)
            {
                var now = DateTimeOffset.UtcNow;
                while (_scheduled.TryPeek(out _, out var due) && due <= now)
                {
                    var item = _scheduled.Dequeue();
                    _ready.Writer.TryWrite(item);
                }

                wait = _scheduled.TryPeek(out _, out var next)
                    ? Max(next - DateTimeOffset.UtcNow, TimeSpan.Zero)
                    : Timeout.InfiniteTimeSpan;
            }

            try
            {
                // Wakes early when a new item is scheduled; otherwise times out at the next due time.
                await _wakeup.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    public void Dispose()
    {
        _cts.Cancel();
        _ready.Writer.TryComplete();
        _cts.Dispose();
        _wakeup.Dispose();
    }
}
