using System.Collections.Concurrent;
using Koan.Communication.Tests.Support;
using Koan.Data.Core;

namespace Koan.Communication.Tests.Specs;

/// <summary>ARCH-0142: an event or a snapshot published inside a scope waits for its commit, as a copy, and a rollback takes it away.</summary>
public sealed class ScopedPublicationSpec
{
    public sealed record Approved(string Note);

    public sealed class ScopedOrder : Entity<ScopedOrder>, IAmbientExempt
    {
        public string Name { get; set; } = "";
    }

    public sealed class Seen
    {
        public ConcurrentQueue<string> Events { get; } = new();
        public ConcurrentQueue<string> Snapshots { get; } = new();
        public TaskCompletionSource FirstEvent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstSnapshot { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class Handler(Seen seen) : IHandleEntityEvent<ScopedOrder, Approved>
    {
        public Task Handle(ScopedOrder entity, EventOccurrence<Approved> occurrence, CancellationToken ct)
        {
            seen.Events.Enqueue($"{entity.Name}/{occurrence.Details?.Note}");
            seen.FirstEvent.TrySetResult();
            return Task.CompletedTask;
        }
    }

    public sealed class Receiver(Seen seen) : IReceiveEntity<ScopedOrder>
    {
        public Task Receive(ScopedOrder entity, CancellationToken ct)
        {
            seen.Snapshots.Enqueue(entity.Name);
            seen.FirstSnapshot.TrySetResult();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_rollback_takes_it_away()
    {
        var ct = TestContext.Current.CancellationToken;
        var seen = new Seen();
        await using var host = await CommunicationTestHost.Start(seen, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        using (EntityContext.Transaction("rolled-back"))
        {
            var order = new ScopedOrder { Name = "never" };
            await order.Save(ct);
            var raised = await order.Events.Raise(new Approved("no"), ct);
            var sent = await order.Transport.Send(ct);
            raised.AwaitsCommit.Should().BeTrue();
            raised.Accepted.Should().Be(0);
            raised.Enumerated.Should().Be(1);
            sent.AwaitsCommit.Should().BeTrue();
            var settle = () => raised.WaitForSettlement(ct);
            (await settle.Should().ThrowAsync<EventException>()).Which.Message.Should().Contain("awaits its scope's commit");
            await EntityContext.Rollback(ct);
        }

        await Task.Delay(200, ct);
        seen.Events.Should().BeEmpty();
        seen.Snapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task A_commit_publishes_a_copy_of_what_was_raised_and_nothing_before_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var seen = new Seen();
        await using var host = await CommunicationTestHost.Start(seen, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        using (EntityContext.Transaction("committed"))
        {
            var order = new ScopedOrder { Name = "as raised" };
            await order.Save(ct);
            var details = new Approved("as raised");
            await order.Events.Raise(details, ct);
            await order.Transport.Send(ct);
            order.Name = "changed after";
            await Task.Delay(100, ct);
            seen.Events.Should().BeEmpty("the scope has not committed");
            seen.Snapshots.Should().BeEmpty("the scope has not committed");
            await EntityContext.Commit(ct);
        }

        await seen.FirstEvent.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        await seen.FirstSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        seen.Events.Should().Equal("as raised/as raised");
        seen.Snapshots.Should().Equal("as raised");
    }

    [Fact]
    public async Task A_scope_that_has_committed_holds_nothing_back_though_its_block_has_yet_to_close()
    {
        var ct = TestContext.Current.CancellationToken;
        var seen = new Seen();
        await using var host = await CommunicationTestHost.Start(seen, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        using (EntityContext.Transaction("committed"))
        {
            await EntityContext.Commit(ct);
            var raised = await new ScopedOrder { Name = "after" }.Events.Raise(new Approved("after"), ct);
            raised.AwaitsCommit.Should().BeFalse();
            raised.Accepted.Should().Be(1);
        }
    }

    [Fact]
    public async Task Outside_a_scope_nothing_waits()
    {
        var ct = TestContext.Current.CancellationToken;
        var seen = new Seen();
        await using var host = await CommunicationTestHost.Start(seen, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        var raised = await new ScopedOrder { Name = "now" }.Events.Raise(new Approved("now"), ct);
        raised.AwaitsCommit.Should().BeFalse();
        raised.Accepted.Should().Be(1);
    }
}
