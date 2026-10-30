using System.Collections.Concurrent;
using System.Diagnostics;

namespace Koan.Communication.Tests.Support;

public sealed class BurstState
{
    public ConcurrentQueue<int> Received { get; } = new();
    public ConcurrentQueue<int> Tallied { get; } = new();
    public TaskCompletionSource<bool> Stalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ManualResetEventSlim Gate { get; } = new(false);
    public int StalledInvocations;
}

/// <summary>Handlers for bursts far larger than any count bound: one that does nothing, one that takes a millisecond.</summary>
public static class BurstFixtures
{
    public sealed class BurstOrder : Entity<BurstOrder>, IAmbientExempt
    {
        public int Value { get; set; }
    }

    public sealed record Burst;

    public sealed record SlowBurst;

    public sealed record StalledBurst;

    public sealed record ThreadBlocked;

    public sealed class IdleSubscription(BurstState state) : IHandleEntityEvent<BurstOrder, Burst>
    {
        public Task Handle(BurstOrder order, EventOccurrence<Burst> occurrence, CancellationToken ct)
        {
            state.Received.Enqueue(order.Value);
            return Task.CompletedTask;
        }
    }

    public sealed class MillisecondSubscription(BurstState state) : IHandleEntityEvent<BurstOrder, SlowBurst>
    {
        public Task Handle(BurstOrder order, EventOccurrence<SlowBurst> occurrence, CancellationToken ct)
        {
            var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000;
            while (Stopwatch.GetTimestamp() < until) Thread.SpinWait(32);
            state.Received.Enqueue(order.Value);
            return Task.CompletedTask;
        }
    }

    public sealed class StallingBurstSubscription(BurstState state) : IHandleEntityEvent<BurstOrder, StalledBurst>
    {
        public async Task Handle(BurstOrder order, EventOccurrence<StalledBurst> occurrence, CancellationToken ct)
        {
            if (Interlocked.Increment(ref state.StalledInvocations) == 1)
            {
                state.Stalled.TrySetResult(true);
                await state.Release.Task.WaitAsync(ct);
            }

            state.Received.Enqueue(order.Value);
        }
    }

    public sealed class TallyBurstSubscription(BurstState state) : IHandleEntityEvent<BurstOrder, StalledBurst>
    {
        public Task Handle(BurstOrder order, EventOccurrence<StalledBurst> occurrence, CancellationToken ct)
        {
            state.Tallied.Enqueue(order.Value);
            return Task.CompletedTask;
        }
    }

    /// <summary>Blocks its thread, not a task: it neither awaits nor reads its cancellation token.</summary>
    public sealed class ThreadBlockingSubscription(BurstState state) : IHandleEntityEvent<BurstOrder, ThreadBlocked>
    {
        public Task Handle(BurstOrder order, EventOccurrence<ThreadBlocked> occurrence, CancellationToken ct)
        {
            state.Stalled.TrySetResult(true);
            state.Gate.Wait();
            return Task.CompletedTask;
        }
    }
}
