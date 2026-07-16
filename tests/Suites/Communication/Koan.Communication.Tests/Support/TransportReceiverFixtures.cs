using Koan.Tenancy;

namespace Koan.Communication.Tests.Support;

public static class TransportReceiverFixtures
{
    public sealed class CopyOrder : Entity<CopyOrder>
    {
        public string Name { get; set; } = "";
    }

    public sealed class CopyAReceiver(TransportTestState state) : IReceiveEntity<CopyOrder>
    {
        public Task Receive(CopyOrder order, CancellationToken ct)
        {
            state.CopyObservations.Enqueue("A:" + order.Name);
            order.Name = "changed-by-a";
            return Task.CompletedTask;
        }
    }

    public sealed class CopyBReceiver(TransportTestState state) : IReceiveEntity<CopyOrder>
    {
        public Task Receive(CopyOrder order, CancellationToken ct)
        {
            state.CopyObservations.Enqueue("B:" + order.Name);
            return Task.CompletedTask;
        }
    }

    public sealed class BlockingOrder : Entity<BlockingOrder>
    {
        public string Name { get; set; } = "";
    }

    public sealed class BlockingReceiver(TransportTestState state) : IReceiveEntity<BlockingOrder>
    {
        public async Task Receive(BlockingOrder order, CancellationToken ct)
        {
            state.BlockingStarted.TrySetResult(true);
            await state.BlockingRelease.Task.WaitAsync(ct);
            state.BlockingObservations.Enqueue(order.Name);
        }
    }

    public sealed class SequenceOrder : Entity<SequenceOrder>
    {
        public int Value { get; set; }
    }

    public sealed class SequenceReceiver(TransportTestState state) : IReceiveEntity<SequenceOrder>
    {
        public Task Receive(SequenceOrder order, CancellationToken ct)
        {
            state.SequenceObservations.Enqueue(order.Value);
            return Task.CompletedTask;
        }
    }

    public sealed class FilterOrder : Entity<FilterOrder>
    {
        public bool Accepted { get; set; }
    }

    public sealed class FilterReceiver(TransportTestState state) : IReceiveEntity<FilterOrder>
    {
        public bool Where(FilterOrder order) => order.Accepted;

        public Task Receive(FilterOrder order, CancellationToken ct)
        {
            Interlocked.Increment(ref state.FilterHandled);
            return Task.CompletedTask;
        }
    }

    public sealed class TenantOrder : Entity<TenantOrder>;

    public sealed class TenantReceiver(TransportTestState state) : IReceiveEntity<TenantOrder>
    {
        public Task Receive(TenantOrder order, CancellationToken ct)
        {
            state.TenantObservations.Enqueue(Tenant.Current?.Id);
            return Task.CompletedTask;
        }
    }

    public sealed class FailureOrder : Entity<FailureOrder>;

    public sealed class FailureReceiver : IReceiveEntity<FailureOrder>
    {
        public Task Receive(FailureOrder order, CancellationToken ct)
            => throw new InvalidOperationException("expected receiver failure");
    }

    public sealed class CancellationOrder : Entity<CancellationOrder>
    {
        public int Value { get; set; }
    }

    public sealed class CancellationReceiver(TransportTestState state) : IReceiveEntity<CancellationOrder>
    {
        public async Task Receive(CancellationOrder order, CancellationToken ct)
        {
            if (Interlocked.Increment(ref state.CancellationHandled) == 1)
            {
                state.CancellationStarted.TrySetResult(true);
                await state.CancellationRelease.Task.WaitAsync(ct);
            }
        }
    }

    public sealed class DrainOrder : Entity<DrainOrder>;

    public sealed class DrainReceiver(TransportTestState state) : IReceiveEntity<DrainOrder>
    {
        public async Task Receive(DrainOrder order, CancellationToken ct)
        {
            state.DrainStarted.TrySetResult(true);
            await state.DrainRelease.Task.WaitAsync(ct);
            Interlocked.Increment(ref state.DrainHandled);
        }
    }

    public sealed class IsolationOrder : Entity<IsolationOrder>;

    public sealed class IsolationReceiver(TransportTestState state) : IReceiveEntity<IsolationOrder>
    {
        public Task Receive(IsolationOrder order, CancellationToken ct)
        {
            Interlocked.Increment(ref state.IsolationHandled);
            return Task.CompletedTask;
        }
    }

    public sealed class NoReceiverOrder : Entity<NoReceiverOrder>;
}
