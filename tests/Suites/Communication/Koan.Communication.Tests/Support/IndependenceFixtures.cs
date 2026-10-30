using System.Collections.Concurrent;

namespace Koan.Communication.Tests.Support;

public sealed class IndependenceState
{
    public ConcurrentQueue<string> Observations { get; } = new();
    public ConcurrentQueue<int> PayloadLengths { get; } = new();

    public TaskCompletionSource<bool> Stalled { get; } = Signal();
    public TaskCompletionSource<bool> Release { get; } = Signal();

    public int StalledInvocations;
    public int TimeoutInvocations;

    private static TaskCompletionSource<bool> Signal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Handlers that stall without honoring their cancellation token, beside handlers that finish at once, so a spec
/// can show that one binding's stall never reaches another binding.
/// </summary>
public static class IndependenceFixtures
{
    public sealed class StallOrder : Entity<StallOrder>, IAmbientExempt
    {
        public string Name { get; set; } = "";
    }

    public sealed record Stall;

    public sealed record Prompt;

    public sealed class StallingSubscription(IndependenceState state) : IHandleEntityEvent<StallOrder, Stall>
    {
        public async Task Handle(StallOrder order, EventOccurrence<Stall> occurrence, CancellationToken ct)
        {
            Interlocked.Increment(ref state.StalledInvocations);
            state.Stalled.TrySetResult(true);
            await state.Release.Task;
        }
    }

    public sealed class PromptSubscription(IndependenceState state) : IHandleEntityEvent<StallOrder, Prompt>
    {
        public Task Handle(StallOrder order, EventOccurrence<Prompt> occurrence, CancellationToken ct)
        {
            state.Observations.Enqueue("prompt:" + order.Name);
            return Task.CompletedTask;
        }
    }

    public sealed class StallTransportOrder : Entity<StallTransportOrder>, IAmbientExempt
    {
        public string Name { get; set; } = "";
    }

    public sealed class StallingReceiver(IndependenceState state) : IReceiveEntity<StallTransportOrder>
    {
        public async Task Receive(StallTransportOrder order, CancellationToken ct)
        {
            Interlocked.Increment(ref state.StalledInvocations);
            state.Stalled.TrySetResult(true);
            await state.Release.Task;
        }
    }

    public sealed class TallyReceiver(IndependenceState state) : IReceiveEntity<StallTransportOrder>
    {
        public Task Receive(StallTransportOrder order, CancellationToken ct)
        {
            state.Observations.Enqueue("tally:" + order.Name);
            return Task.CompletedTask;
        }
    }

    public sealed class CrowdOrder : Entity<CrowdOrder>, IAmbientExempt
    {
        public string Name { get; set; } = "";
    }

    public sealed record Jammed;

    public sealed class StallingJamSubscription(IndependenceState state) : IHandleEntityEvent<CrowdOrder, Jammed>
    {
        public async Task Handle(CrowdOrder order, EventOccurrence<Jammed> occurrence, CancellationToken ct)
        {
            if (Interlocked.Increment(ref state.StalledInvocations) == 1)
            {
                state.Stalled.TrySetResult(true);
                await state.Release.Task;
            }

            state.Observations.Enqueue("jam:" + order.Name);
        }
    }

    public sealed class LargeOrder : Entity<LargeOrder>, IAmbientExempt
    {
        public string Body { get; set; } = "";
    }

    public sealed record LargeRaised;

    public sealed class LargeReceiver(IndependenceState state) : IReceiveEntity<LargeOrder>
    {
        public Task Receive(LargeOrder order, CancellationToken ct)
        {
            state.PayloadLengths.Enqueue(order.Body.Length);
            return Task.CompletedTask;
        }
    }

    public sealed class LargeSubscription(IndependenceState state) : IHandleEntityEvent<LargeOrder, LargeRaised>
    {
        public Task Handle(LargeOrder order, EventOccurrence<LargeRaised> occurrence, CancellationToken ct)
        {
            state.PayloadLengths.Enqueue(order.Body.Length);
            return Task.CompletedTask;
        }
    }

    public sealed class TimeoutOrder : Entity<TimeoutOrder>, IAmbientExempt
    {
        public int Value { get; set; }
    }

    public sealed record TimedRaised;

    public sealed class OverrunningSubscription(IndependenceState state) : IHandleEntityEvent<TimeoutOrder, TimedRaised>
    {
        public async Task Handle(TimeoutOrder order, EventOccurrence<TimedRaised> occurrence, CancellationToken ct)
        {
            if (Interlocked.Increment(ref state.TimeoutInvocations) == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            state.Observations.Enqueue("timed:" + order.Value);
        }
    }
}
