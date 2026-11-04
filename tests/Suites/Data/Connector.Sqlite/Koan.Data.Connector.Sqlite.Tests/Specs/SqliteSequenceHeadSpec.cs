using System.Diagnostics;
using Koan.Core.Naming;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using static Koan.Data.Connector.Sqlite.Tests.Specs.SqliteCommandSpec;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>A container axis over slips, as a separate-container tenancy registers one.</summary>
internal sealed class SlipContainer : IStorageNameParticleContributor
{
    internal static readonly AsyncLocal<string?> Value = new();

    public string Axis => "slip-desk";

    public Particle? GetParticle(Type entityType) =>
        entityType == typeof(CommandSlip) && Value.Value is { } desk ? new Particle(100, Axis, desk, ParticlePosition.Leading, "-") : null;
}

/// <summary>
/// DATA-0169: a sequence has a committed head, kept per host and raised once by each hold that commits, before its sender is answered, and a reader
/// waits on it for a bound it gives. The head lags what has committed and never leads it: every number it answers is a row a plain read finds.
/// </summary>
public sealed class SqliteSequenceHeadSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WellInside = TimeSpan.FromSeconds(5);

    private static Sequence Numbers => CommandSlip.Numbers;

    [Fact]
    public async Task The_head_first_asked_is_the_committed_top_and_a_send_has_raised_it_when_it_returns()
    {
        RequireBackingStore();
        var path = TempDatabase("head");
        try
        {
            await using var host = await Start(Settings(path));
            // Plain saves outside a hold commit rows no hold tallies, so only the store can tell the first ask about them.
            for (var number = 1; number <= 3; number++)
                await new CommandSlip { Number = number, Body = "plain" }.Save(Cancellation);

            (await Numbers.Head(Cancellation)).Should().Be(3, "the first ask reads the committed top");
            Done(await new NumberASlip("sent", 0).Send(Cancellation)).Should().Be(4);
            (await Numbers.Head(Cancellation)).Should().Be(4, "the hold raised the head before its sender was answered");

            await new CommandSlip { Number = 5, Body = "plain" }.Save(Cancellation);
            (await Numbers.Head(Cancellation)).Should().Be(4, "after the first ask the head answers from memory, and nobody waited on it");
            Done(await new NumberASlip("sent", 1).Send(Cancellation)).Should().Be(6);
            (await Numbers.Head(Cancellation)).Should().Be(6);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_refused_a_thrown_and_a_rolled_back_command_leave_the_head_where_it_was()
    {
        RequireBackingStore();
        var path = TempDatabase("head-unmoved");
        try
        {
            await using var host = await Start(Settings(path));
            Done(await new NumberASlip("first", 0).Send(Cancellation)).Should().Be(1);

            (await new SaveThenAnswer("refused", new Outcome<string>.Refused(Reason.Gated("no"))).Send(Cancellation)).Should().BeOfType<Outcome<string>.Refused>();
            (await Numbers.Head(Cancellation)).Should().Be(1, "a refused command took a number and gave it back");
            (await new CreateSlip("thrown-refusal", "s", 0, "").Send(Cancellation)).Should().BeOfType<Outcome<CommandSlip>.Refused>();
            (await Numbers.Head(Cancellation)).Should().Be(1, "a refusal thrown from below the command gave its number back");
            (await new WriteSlipAndStub("thrown", () => new InvalidOperationException("thrown")).Send(Cancellation)).Should().BeOfType<Outcome<string>.Failed>();
            (await Numbers.Head(Cancellation)).Should().Be(1, "a thrown command gave its number back");
            using (await EntityContext.Hold("rolled-back", Cancellation))
                await new CommandSlip { Number = await Numbers.Next(Cancellation), Body = "rolled back" }.Save(Cancellation);
            (await Numbers.Head(Cancellation)).Should().Be(1, "a hold disposed without a commit rolled back");

            Done(await new NumberASlip("next", 0).Send(Cancellation)).Should().Be(2);
            (await Numbers.Head(Cancellation)).Should().Be(2);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_wait_wakes_when_a_command_commits_past_it_and_answers_at_its_bound_when_none_does()
    {
        RequireBackingStore();
        var path = TempDatabase("head-wait");
        try
        {
            await using var host = await Start(Settings(path));
            Done(await new NumberASlip("first", 0).Send(Cancellation));
            var head = await Numbers.Head(Cancellation);

            var clock = Stopwatch.StartNew();
            var waiting = Numbers.WaitPast(head, Wait, Cancellation);
            (await Task.WhenAny(waiting, Task.Delay(200, Cancellation))).Should().NotBe(waiting, "nothing has committed past it");
            Done(await new NumberASlip("woke it", 0).Send(Cancellation));
            (await waiting.WaitAsync(Bound, Cancellation)).Should().Be(head + 1);
            clock.Elapsed.Should().BeLessThan(WellInside, "the commit woke the wait; only a missed wake answers at the bound, from the store");

            var quiet = TimeSpan.FromMilliseconds(300);
            clock.Restart();
            (await Numbers.WaitPast(head + 1, quiet, Cancellation).WaitAsync(Bound, Cancellation)).Should().Be(head + 1);
            clock.Elapsed.Should().BeGreaterThanOrEqualTo(quiet).And.BeLessThan(WellInside, "with nothing committed it answers at its bound");

            using var gaveUp = new CancellationTokenSource();
            var cancelled = Numbers.WaitPast(head + 1, Wait, gaveUp.Token);
            gaveUp.Cancel();
            await FluentActions.Awaiting(() => cancelled.WaitAsync(Bound, Cancellation)).Should().ThrowAsync<OperationCanceledException>();
        }
        finally { Delete(path); }
    }

    /// <summary>The wait takes its wake before it reads the head. Taken after, the commit this spec lands between the two would be missed,
    /// and the wait would answer only at its bound.</summary>
    [Fact]
    public async Task A_commit_between_a_waits_read_of_the_head_and_its_wait_still_wakes_it()
    {
        RequireBackingStore();
        var path = TempDatabase("head-between");
        try
        {
            await using var host = await Start(Settings(path));
            var heads = host.Services.GetRequiredService<SequenceHeads>();
            var head = await Numbers.Head(Cancellation);
            heads.BetweenReadAndWait = async () =>
            {
                heads.BetweenReadAndWait = null;
                Done(await new NumberASlip("between", 0).Send(Cancellation));
            };

            var clock = Stopwatch.StartNew();
            (await Numbers.WaitPast(head, Wait, Cancellation).WaitAsync(Bound, Cancellation)).Should().Be(head + 1);
            clock.Elapsed.Should().BeLessThan(WellInside, "the wake was taken before the read, so the commit between them ended the wait");
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task During_a_burst_every_head_answered_is_a_row_a_plain_read_finds_and_the_burst_ends_at_its_last_number()
    {
        RequireBackingStore();
        var path = TempDatabase("head-burst");
        try
        {
            await using var host = await Start(Settings(path));
            const int burst = 200;

            var sending = Task.WhenAll(Enumerable.Range(0, burst).Select(ordinal => Task.Run(() => new NumberASlip("burst", ordinal).Send(Cancellation), Cancellation)));
            var waited = Task.Run(async () =>
            {
                var answered = new List<long>();
                for (var head = 0L; head < burst;)
                {
                    head = await Numbers.WaitPast(head, Wait, Cancellation);
                    (await CommandSlip.Query(slip => slip.Number <= head, Cancellation)).Should().HaveCount((int)head, "every number up to the head has committed");
                    answered.Add(head);
                }
                return answered;
            }, Cancellation);
            var asked = 0;
            while (!sending.IsCompleted)
            {
                var head = await Numbers.Head(Cancellation);
                if (head > 0) (await CommandSlip.Query(slip => slip.Number == head, Cancellation)).Should().ContainSingle("the head names a committed row");
                asked++;
            }

            (await sending.WaitAsync(TimeSpan.FromMinutes(2), Cancellation)).Select(Done).Order().Should().Equal(Enumerable.Range(1, burst).Select(number => (long)number));
            var answers = await waited.WaitAsync(Bound, Cancellation);
            Output.WriteLine($"{asked} asks and {answers.Count} waits answered during a burst of {burst}");
            answers.Should().BeInAscendingOrder().And.HaveCountGreaterThan(1);
            (await Numbers.Head(Cancellation)).Should().Be(burst);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task Two_hosts_in_one_process_keep_two_heads()
    {
        RequireBackingStore();
        var (here, there) = (TempDatabase("head-host-a"), TempDatabase("head-host-b"));
        try
        {
            await using var first = await Start(Settings(here));
            await using var second = await Start(Settings(there));

            using (first.Enter())
            {
                for (var ordinal = 0; ordinal < 3; ordinal++) Done(await new NumberASlip("first", ordinal).Send(Cancellation));
                (await Numbers.Head(Cancellation)).Should().Be(3);
            }
            using (second.Enter())
            {
                (await Numbers.Head(Cancellation)).Should().Be(0, "the other host's commits are not this host's head");
                Done(await new NumberASlip("second", 0).Send(Cancellation)).Should().Be(1);
                (await Numbers.Head(Cancellation)).Should().Be(1);
            }
            using (first.Enter())
                (await Numbers.Head(Cancellation)).Should().Be(3);
        }
        finally
        {
            Delete(here);
            Delete(there);
        }
    }

    /// <summary>Another process's commit reaches no head of this one by itself; a wait that ends at its bound reads the store and finds it.</summary>
    [Fact]
    public async Task A_commit_by_another_process_reaches_a_waiting_head_within_one_bound()
    {
        RequireBackingStore();
        var path = TempDatabase("head-two-processes");
        try
        {
            await using var host = await Start(Settings(path));
            (await Numbers.Head(Cancellation)).Should().Be(0);

            await using var other = HeldContender.Process.Start("command", path, "1");
            await other.Holding();
            await other.Say("go");
            await other.Finished();
            Output.WriteLine(other.Output);
            (await Numbers.Head(Cancellation)).Should().Be(0, "a head nobody waits on learns of no other process's commit");

            var bound = TimeSpan.FromSeconds(2);
            var clock = Stopwatch.StartNew();
            (await Numbers.WaitPast(0, bound, Cancellation).WaitAsync(Bound, Cancellation)).Should().Be(1);
            clock.Elapsed.Should().BeGreaterThanOrEqualTo(bound).And.BeLessThan(bound + WellInside, "the wait read the store once, at its bound");
            (await Numbers.Head(Cancellation)).Should().Be(1, "the wait raised the head to what it found");
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task The_head_refuses_inside_a_hold_and_from_a_source_an_adapter_or_a_partition_whose_holds_raise_nothing()
    {
        RequireBackingStore();
        var (path, elsewhere) = (TempDatabase("head-refusals"), TempDatabase("head-refusals-other"));
        try
        {
            var settings = Settings(path);
            settings["Koan:Data:Sources:Other:Adapter"] = "sqlite";
            settings["Koan:Data:Sources:Other:ConnectionString"] = $"Data Source={elsewhere}";
            await using var host = await Start(settings);
            Done(await new NumberASlip("default", 0).Send(Cancellation)).Should().Be(1);

            using (await EntityContext.Hold("inside", Cancellation))
            {
                (await FluentActions.Awaiting(() => Numbers.Head(Cancellation)).Should().ThrowAsync<InvalidOperationException>())
                    .Which.Message.Should().Contain("Inside a hold, Highest is the read").And.Contain("ask the head before the hold or after it");
                await FluentActions.Awaiting(() => Numbers.WaitPast(0, Wait, Cancellation)).Should().ThrowAsync<InvalidOperationException>();
            }

            using (EntityContext.Source("Other"))
            {
                await new CommandSlip { Id = "made", Number = 0, Body = "makes the table" }.Save(Cancellation);
                (await FluentActions.Awaiting(() => Numbers.Head(Cancellation)).Should().ThrowAsync<InvalidOperationException>())
                    .Which.Message.Should().Contain("from source 'Other'").And.Contain("Ask the head with no source, adapter, partition or container in scope");
                Done(await new NumberASlip("other", 0).Send(Cancellation)).Should().Be(1);
                Done(await new NumberASlip("other", 1).Send(Cancellation)).Should().Be(2);
            }
            using (EntityContext.Adapter("sqlite"))
                (await FluentActions.Awaiting(() => Numbers.WaitPast(0, Wait, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
                    .Which.Message.Should().Contain("from adapter 'sqlite'");
            using (EntityContext.Partition("north"))
            {
                await new CommandSlip { Id = "made", Number = 0, Body = "makes the table" }.Save(Cancellation);
                (await FluentActions.Awaiting(() => Numbers.Head(Cancellation)).Should().ThrowAsync<InvalidOperationException>())
                    .Which.Message.Should().Contain("from partition 'north'");
                Done(await new NumberASlip("north", 0).Send(Cancellation)).Should().Be(1);
                Done(await new NumberASlip("north", 1).Send(Cancellation)).Should().Be(2);
            }

            (await Numbers.Head(Cancellation)).Should().Be(1, "holds that numbered another store raised nothing here");
            await FluentActions.Awaiting(() => Numbers.WaitPast(1, TimeSpan.Zero, Cancellation)).Should().ThrowAsync<ArgumentOutOfRangeException>()
                .WithMessage("*give a positive, finite bound*");
            await FluentActions.Awaiting(() => Numbers.WaitPast(1, Timeout.InfiniteTimeSpan, Cancellation)).Should().ThrowAsync<ArgumentOutOfRangeException>();
            await FluentActions.Awaiting(() => Numbers.WaitPast(1, TimeSpan.MaxValue, Cancellation)).Should().ThrowAsync<ArgumentOutOfRangeException>()
                .WithMessage("*give a positive, finite bound*");
        }
        finally
        {
            Delete(path);
            Delete(elsewhere);
        }
    }

    [Fact]
    public async Task The_head_refuses_where_a_container_axis_names_another_container()
    {
        RequireBackingStore();
        var path = TempDatabase("head-container");
        try
        {
            await using var host = await Start(Settings(path), declare: () => StorageNameParticleRegistry.Register(new SlipContainer()));
            Done(await new NumberASlip("default", 0).Send(Cancellation)).Should().Be(1);

            SlipContainer.Value.Value = "east";
            try
            {
                (await FluentActions.Awaiting(() => Numbers.Head(Cancellation)).Should().ThrowAsync<InvalidOperationException>())
                    .Which.Message.Should().Contain("the container its 'slip-desk' axis names");
            }
            finally { SlipContainer.Value.Value = null; }
            (await Numbers.Head(Cancellation)).Should().Be(1, "with no value in the flow the axis names the default container");
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task The_head_refuses_an_entity_with_an_equality_field_even_with_no_value_in_the_flow()
    {
        RequireBackingStore();
        var path = TempDatabase("head-equality");
        try
        {
            await using var host = await Start(Settings(path), declare: () => ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
                StorageName: "__desk",
                ClrType: typeof(string),
                ValueProvider: () => null,
                AppliesTo: type => type == typeof(CommandSlip),
                RequiredCapability: DataCaps.Isolation.RowScoped)));

            (await FluentActions.Awaiting(() => Numbers.Head(Cancellation)).Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().Contain("within each value of its field '__desk'").And.Contain("whatever value this flow carries");
            Done(await new NumberASlip("desk", 0).Send(Cancellation)).Should().Be(1, "it still numbers inside a hold");
        }
        finally { Delete(path); }
    }
}
