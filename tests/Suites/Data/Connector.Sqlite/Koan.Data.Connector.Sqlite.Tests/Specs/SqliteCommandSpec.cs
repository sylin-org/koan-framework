using System.Diagnostics;
using Koan.Core;
using Koan.Core.BackgroundServices;
using Koan.Core.Context;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Transactions;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

[Storage(Name = "KOAN_COMMAND_SLIP")]
public sealed class CommandSlip : Entity<CommandSlip>
{
    public static readonly Reason NeedsBody = Reason.Invalid("A slip needs a body.");

    public static readonly Sequence Numbers = Sequence.Of<CommandSlip>(slip => slip.Number);

    [Index(Name = "ix_command_slip_number", Unique = true)] public long Number { get; set; }
    public string Sender { get; set; } = "";
    public long Ordinal { get; set; }
    public string Body { get; set; } = "";

    /// <summary>What an entity does when it refuses: it knows no command's result type, so it throws.</summary>
    public static CommandSlip Write(string id, string sender, long ordinal, string body, long number) =>
        string.IsNullOrWhiteSpace(body)
            ? throw new Refusal(NeedsBody)
            : new CommandSlip { Id = id, Sender = sender, Ordinal = ordinal, Body = body, Number = number };
}

[Storage(Name = "KOAN_COMMAND_STUB")]
public sealed class CommandStub : Entity<CommandStub>
{
    public string Note { get; set; } = "";
}

/// <summary>Numbers a slip, the same id answering with what is there, and another body being a conflict.</summary>
public sealed record CreateSlip(string Id, string Sender, int Ordinal, string Body) : ICommand<CommandSlip>
{
    public async Task<Outcome<CommandSlip>> Run(CancellationToken ct)
    {
        if (await CommandSlip.Get(Id, ct) is { } existing)
            return existing.Body == Body
                ? new Outcome<CommandSlip>.Done(existing)
                : new Outcome<CommandSlip>.Refused(Reason.Conflict($"Slip '{Id}' already exists with a different body."));
        // The number is taken before the entity can refuse, so a refusal proves the hold gives it back.
        return new Outcome<CommandSlip>.Done(await CommandSlip.Write(Id, Sender, Ordinal, Body, await CommandSlip.Numbers.Next(ct)).Save(ct));
    }
}

/// <summary>Two entity types in one command, and what it does between them.</summary>
public sealed record WriteSlipAndStub(string Id, Func<Exception?>? Between = null) : ICommand<string>
{
    public async Task<Outcome<string>> Run(CancellationToken ct)
    {
        await new CommandSlip { Id = Id, Sender = "pair", Body = "slip", Number = await CommandSlip.Numbers.Next(ct) }.Save(ct);
        if (Between?.Invoke() is { } thrown) throw thrown;
        await new CommandStub { Id = Id, Note = "stub" }.Save(ct);
        return new Outcome<string>.Done(Id);
    }
}

/// <summary>Saves, then answers with whatever outcome it is told to, so every outcome but Done proves it leaves nothing.</summary>
public sealed record SaveThenAnswer(string Id, Outcome<string> Answer) : ICommand<string>
{
    public async Task<Outcome<string>> Run(CancellationToken ct)
    {
        await new CommandSlip { Id = Id, Sender = "answer", Body = "slip", Number = await CommandSlip.Numbers.Next(ct) }.Save(ct);
        return Answer;
    }
}

/// <summary>Reads what it has written, through a keyed read and a query, before anything is committed.</summary>
public sealed record ReadOwnWrites(string Id) : ICommand<(string? Read, long Count, long Second)>
{
    public async Task<Outcome<(string? Read, long Count, long Second)>> Run(CancellationToken ct)
    {
        await new CommandSlip { Id = Id, Sender = "reads", Body = "own write", Number = await CommandSlip.Numbers.Next(ct) }.Save(ct);
        var read = await CommandSlip.Get(Id, ct);
        var count = await CommandSlip.Count.Exact(ct);
        return new Outcome<(string? Read, long Count, long Second)>.Done((read?.Body, count, await CommandSlip.Numbers.Next(ct)));
    }
}

/// <summary>Saves a slip, says so, and keeps its turn until the spec lets it go or its sender gives up.</summary>
public sealed class HoldTheTurn(string? id = null) : ICommand<bool>
{
    public TaskCompletionSource Running { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<Outcome<bool>> Run(CancellationToken ct)
    {
        if (id is not null) await new CommandSlip { Id = id, Sender = "held", Body = "held", Number = await CommandSlip.Numbers.Next(ct) }.Save(ct);
        Running.TrySetResult();
        await Release.Task.WaitAsync(ct);
        return new Outcome<bool>.Done(true);
    }
}

/// <summary>Writes a row a deferred foreign key refuses at COMMIT, so the store refuses the native commit itself.</summary>
public sealed record CommitTheStoreRefuses(string Id) : ICommand<bool>
{
    public async Task<Outcome<bool>> Run(CancellationToken ct)
    {
        await new CommandSlip { Id = Id, Sender = "refused", Body = "never", Number = await CommandSlip.Numbers.Next(ct) }.Save(ct);
        await Data<CommandSlip, string>.Execute<int>(
            InstructionSql.NonQuery("INSERT INTO koan_command_child (parent) VALUES ('missing')", DataOperationEffect.Write), ct);
        return new Outcome<bool>.Done(true);
    }
}

/// <summary>Sends another command from inside its own turn, and answers with what that send was told.</summary>
public sealed record SendFromInside : ICommand<Outcome<CommandSlip>>
{
    public async Task<Outcome<Outcome<CommandSlip>>> Run(CancellationToken ct) =>
        new Outcome<Outcome<CommandSlip>>.Done(await new CreateSlip("inner", "inside", 0, "body").Send(ct));
}

/// <summary>Numbers a slip and throws nothing: what two processes run against one file.</summary>
public sealed record NumberASlip(string Sender, int Ordinal) : ICommand<long>
{
    public async Task<Outcome<long>> Run(CancellationToken ct)
    {
        var number = await CommandSlip.Numbers.Next(ct);
        await new CommandSlip { Sender = Sender, Ordinal = Ordinal, Body = "numbered", Number = number }.Save(ct);
        return new Outcome<long>.Done(number);
    }
}

/// <summary>Reads nothing and writes nothing, so the difference from <see cref="NumberASlip"/> is what a number costs.</summary>
public sealed record SaveAStub : ICommand<bool>
{
    public async Task<Outcome<bool>> Run(CancellationToken ct)
    {
        await new CommandStub { Note = "unnumbered" }.Save(ct);
        return new Outcome<bool>.Done(true);
    }
}

[Storage(Name = "KOAN_COMMAND_TICKET")]
public sealed class CommandTicket : Entity<CommandTicket>
{
    public static readonly Sequence Serials = Sequence.Of<CommandTicket>(ticket => ticket.Serial);

    [Index(Name = "ix_command_ticket_serial", Unique = true)] public long Serial { get; set; }
}

/// <summary>Takes from two sequences in one command, then answers as it is told.</summary>
public sealed record NumberTwoThings(Outcome<(long Number, long Serial)>? Answer = null) : ICommand<(long Number, long Serial)>
{
    public async Task<Outcome<(long Number, long Serial)>> Run(CancellationToken ct)
    {
        var number = await CommandSlip.Numbers.Next(ct);
        var serial = await CommandTicket.Serials.Next(ct);
        await new CommandSlip { Number = number, Body = "numbered" }.Save(ct);
        await new CommandTicket { Serial = serial }.Save(ct);
        return Answer ?? new Outcome<(long Number, long Serial)>.Done((number, serial));
    }
}

/// <summary>Registers work to follow its commit that awaits another send, which runs: the store's turn has passed on by then.</summary>
public sealed class SendAfterItsCommit(string id) : ICommand<bool>
{
    public TaskCompletionSource<Outcome<CommandSlip>> Answered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<Outcome<bool>> Run(CancellationToken ct)
    {
        await new CommandSlip { Id = id, Sender = "after", Body = "slip", Number = await CommandSlip.Numbers.Next(ct) }.Save(ct);
        EntityContext.CurrentTransaction!.AfterCommit(async _ => Answered.SetResult(await new CreateSlip(id + "-next", "after", 0, "body").Send()));
        return new Outcome<bool>.Done(true);
    }
}

/// <summary>Saves, then waits with no token, by await or by blocking its thread, and tries to save again when it is let go.</summary>
public sealed class IgnoresItsToken(string id, bool blocks) : ICommand<bool>
{
    public TaskCompletionSource Running { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<Exception?> LateWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<Outcome<bool>> Run(CancellationToken ct)
    {
        await new CommandSlip { Id = id, Sender = "stuck", Body = "first", Number = await CommandSlip.Numbers.Next() }.Save();
        Running.TrySetResult();
        if (blocks) Release.Task.Wait();
        else await Release.Task;
        try
        {
            await new CommandSlip { Id = id + "-late", Sender = "stuck", Body = "late", Number = 1000 }.Save();
            LateWrite.TrySetResult(null);
        }
        catch (Exception refused)
        {
            LateWrite.TrySetResult(refused);
        }
        return new Outcome<bool>.Done(true);
    }
}

/// <summary>Commits, with work to follow its commit that never ends.</summary>
public sealed record HangAfterItsCommit(string Id) : ICommand<bool>
{
    public async Task<Outcome<bool>> Run(CancellationToken ct)
    {
        await new CommandSlip { Id = Id, Sender = "hangs", Body = "slip", Number = await CommandSlip.Numbers.Next(ct) }.Save(ct);
        EntityContext.CurrentTransaction!.AfterCommit(_ => new TaskCompletionSource().Task);
        return new Outcome<bool>.Done(true);
    }
}

/// <summary>Saves, tries to end its own hold, and then returns what it is told.</summary>
public sealed record EndItsOwnHold(string Id, Func<Task> End, Outcome<bool> Answer) : ICommand<bool>
{
    public async Task<Outcome<bool>> Run(CancellationToken ct)
    {
        await new CommandSlip { Id = Id, Sender = "ends", Body = "slip", Number = await CommandSlip.Numbers.Next(ct) }.Save(ct);
        await End();
        return Answer;
    }
}

/// <summary>Answers with the partition and the carried value it finds, and writes where it runs.</summary>
public sealed record WhereAmI(string Id) : ICommand<(string? Partition, string? Carried)>
{
    public async Task<Outcome<(string? Partition, string? Carried)>> Run(CancellationToken ct)
    {
        await new CommandSlip { Id = Id, Sender = "where", Body = "slip", Number = await CommandSlip.Numbers.Next(ct) }.Save(ct);
        return new Outcome<(string? Partition, string? Carried)>.Done((EntityContext.Current?.Partition, CarriedValue.Value.Value));
    }
}

/// <summary>An ambient axis as a module would carry it.</summary>
public sealed class CarriedValue : IKoanContextCarrier
{
    internal static readonly AsyncLocal<string?> Value = new();

    public string AxisKey => "command-spec";
    public ContextIngressTrust MinimumIngressTrust => ContextIngressTrust.HostTrusted;
    public string? Capture() => Value.Value;
    public IDisposable Restore(string captured) => Set(captured);
    public IDisposable Suppress() => Set(null);

    internal static IDisposable Set(string? value)
    {
        var previous = Value.Value;
        Value.Value = value;
        return new Reset(previous);
    }

    private sealed class Reset(string? previous) : IDisposable
    {
        public void Dispose() => Value.Value = previous;
    }
}

/// <summary>A startup service that sends with no token, as a seed does. Generic, so that only a spec that registers it runs it.</summary>
public sealed class SeedAtStartup<TCommand>(Func<Task<Outcome<CommandSlip>>> send) : IKoanStartupService
{
    public TaskCompletionSource<Outcome<CommandSlip>> Answered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Name => "seed";
    public int StartupOrder => 0;
    public async Task Execute(CancellationToken cancellationToken) => Answered.TrySetResult(await send());
}

/// <summary>
/// DATA-0167: an application sends a write as a value, and each runs in a hold of its own, in its sender's flow, and answers with an
/// outcome. The hold is what keeps it correct: reads see the command's own writes, a rollback leaves nothing and gives back its
/// numbers, and every other sender's command takes its turn, in this process and in another.
/// </summary>
public sealed class SqliteCommandSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    internal static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private const string Slips = "KOAN_COMMAND_SLIP";

    internal static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static string TempDatabase(string label) =>
        Path.Combine(Path.GetTempPath(), $"koan-command-{label}-{Guid.CreateVersion7():n}.db");

    internal static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }

    internal static Dictionary<string, string?> Settings(string path, string? connectionSuffix = null, TimeSpan? busyTimeout = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path}{connectionSuffix}",
        };
        if (busyTimeout is { } busy) settings["Koan:Data:Sqlite:BusyTimeout"] = busy.ToString();
        return settings;
    }

    /// <summary>A real host, whose stop a spec can watch, on a file whose tables exist already (DATA-0166: first use belongs at boot).
    /// <paramref name="declare"/> runs inside the host's composition, and <paramref name="configure"/> after it.</summary>
    internal static async Task<IntegrationHost> Start(
        Dictionary<string, string?> settings, bool provision = true, Action? declare = null, Action<IServiceCollection>? configure = null)
    {
        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services =>
            {
                if (declare is null) services.AddKoan();
                else services.AddKoan(declare);
                configure?.Invoke(services);
            })
            .StartAsync(Cancellation);
        if (provision)
        {
            await new CommandSlip { Id = "first", Number = 0, Body = "first" }.Save(Cancellation);
            await new CommandStub { Id = "first" }.Save(Cancellation);
            await CommandSlip.Remove("first", Cancellation);
            await CommandStub.Remove("first", Cancellation);
        }
        return host;
    }

    internal static T Done<T>(Outcome<T> outcome) => outcome.Should().BeOfType<Outcome<T>.Done>().Which.Result;

    /// <summary>What a connection of its own reads.</summary>
    private static async Task<long> CommittedCount(string path, string table)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync(Cancellation);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt64(await command.ExecuteScalarAsync(Cancellation));
    }

    [Fact]
    public async Task Commands_from_several_senders_each_run_once_in_arrival_order_with_one_unbroken_sequence()
    {
        RequireBackingStore();
        var path = TempDatabase("arrival");
        try
        {
            await using var host = await Start(Settings(path));
            const int senders = 4, each = 25;

            var outcomes = await Task.WhenAll(Enumerable.Range(0, senders).Select(sender => Task.Run(() =>
            {
                // Every send reaches the store's turn before the next, so arrival order is this sender's order.
                var sent = Enumerable.Range(0, each).Select(ordinal => new CreateSlip($"s{sender}-{ordinal}", $"s{sender}", ordinal, "body").Send(Cancellation)).ToList();
                return Task.WhenAll(sent);
            }, Cancellation))).WaitAsync(Bound * 2, Cancellation);

            var slips = outcomes.SelectMany(sender => sender).Select(Done).ToList();
            var stored = await CommandSlip.FirstPage(senders * each + 1, sort => sort.OrderBy(slip => slip.Number), Cancellation);

            slips.Should().HaveCount(senders * each);
            stored.Select(slip => slip.Number).Should().Equal(Enumerable.Range(1, senders * each).Select(number => (long)number));
            stored.Select(slip => slip.Id).Should().BeEquivalentTo(slips.Select(slip => slip.Id));
            foreach (var sender in stored.GroupBy(slip => slip.Sender))
                sender.Select(slip => slip.Ordinal).Should().BeInAscendingOrder("a sender's commands ran in the order it sent them");
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_command_commits_two_entity_types_whole_and_nothing_is_visible_outside_until_it_does()
    {
        RequireBackingStore();
        var path = TempDatabase("whole");
        try
        {
            await using var host = await Start(Settings(path));
            var holding = new HoldTheTurn("held");
            var sending = holding.Send(Cancellation);
            await holding.Running.Task.WaitAsync(Bound, Cancellation);

            (await CommittedCount(path, Slips)).Should().Be(0, "a hold's writes are not committed yet");
            (await CommandSlip.Get("held", Cancellation)).Should().BeNull("a read outside the command sees committed state");
            holding.Release.SetResult();
            Done(await sending.WaitAsync(Bound, Cancellation));
            (await CommittedCount(path, Slips)).Should().Be(1);

            Done(await new WriteSlipAndStub("pair").Send(Cancellation)).Should().Be("pair");
            (await CommandSlip.Get("pair", Cancellation)).Should().NotBeNull();
            (await CommandStub.Get("pair", Cancellation)).Should().NotBeNull();
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_throw_leaves_neither_entity_type_and_the_next_command_commits()
    {
        RequireBackingStore();
        var path = TempDatabase("throw");
        try
        {
            await using var host = await Start(Settings(path));

            var thrown = new InvalidOperationException("thrown between the two saves");
            var failed = await new WriteSlipAndStub("pair", () => thrown).Send(Cancellation);

            var failure = failed.Should().BeOfType<Outcome<string>.Failed>().Which;
            failure.Cause.Should().BeSameAs(thrown);
            failure.Landed.Should().Be(DataCommitOutcome.NotCommitted);
            (await CommandSlip.Get("pair", Cancellation)).Should().BeNull();
            (await CommandStub.Get("pair", Cancellation)).Should().BeNull();
            Done(await new CreateSlip("after", "s", 0, "body").Send(Cancellation)).Number.Should().Be(1, "the number the throw took was given back");
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task Every_outcome_but_done_leaves_nothing_whether_returned_or_thrown_and_its_number_is_given_back()
    {
        RequireBackingStore();
        var path = TempDatabase("refusal");
        try
        {
            await using var host = await Start(Settings(path));

            Reason[] reasons = [Reason.Absent("gone"), Reason.Gated("locked"), Reason.Conflict("taken"), Reason.Invalid("not a value")];
            foreach (var reason in reasons)
            {
                var answer = new Outcome<string>.Refused(reason);
                (await new SaveThenAnswer("returned", answer).Send(Cancellation)).Should().Be(answer);
                (await new WriteSlipAndStub("thrown", () => new Refusal(reason)).Send(Cancellation)).Should().Be(answer);
            }

            // A kind is part of what a reason is.
            reasons[3].Kind.Should().Be(ReasonKind.Invalid);
            reasons[0].Kind.Should().Be(ReasonKind.Absent);
            reasons[1].Kind.Should().Be(ReasonKind.Gated);
            reasons[2].Kind.Should().Be(ReasonKind.Conflict);
            Reason.Absent("not a value").Should().NotBe(reasons[3]);

            // An entity refusing from below the command, after the command has taken a number.
            (await new CreateSlip("blank", "s", 0, " ").Send(Cancellation)).Should().Be(new Outcome<CommandSlip>.Refused(CommandSlip.NeedsBody));

            (await CommittedCount(path, Slips)).Should().Be(0);
            Done(await new CreateSlip("written", "s", 1, "body").Send(Cancellation)).Number.Should().Be(1, "no gap across the commands that did not commit");
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task The_same_id_with_the_same_body_answers_with_what_is_there_and_another_body_is_a_conflict()
    {
        RequireBackingStore();
        var path = TempDatabase("same-id");
        try
        {
            await using var host = await Start(Settings(path));

            var first = Done(await new CreateSlip("same", "s", 0, "one").Send(Cancellation));
            var again = Done(await new CreateSlip("same", "s", 0, "one").Send(Cancellation));
            var differs = await new CreateSlip("same", "s", 0, "two").Send(Cancellation);
            var after = Done(await new CreateSlip("next", "s", 1, "three").Send(Cancellation));

            (again.Id, again.Number, again.Body).Should().Be((first.Id, first.Number, first.Body));
            var conflict = differs.Should().BeOfType<Outcome<CommandSlip>.Refused>().Which.Reason;
            conflict.Kind.Should().Be(ReasonKind.Conflict);
            conflict.Text.Should().Contain("'same'");
            (await CommandSlip.Get("same", Cancellation))!.Body.Should().Be("one");
            after.Number.Should().Be(2);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_command_reads_its_own_writes_and_counts_on_from_its_own_numbers()
    {
        RequireBackingStore();
        var path = TempDatabase("own-writes");
        try
        {
            await using var host = await Start(Settings(path));
            Done(await new CreateSlip("earlier", "s", 0, "body").Send(Cancellation));

            var (read, count, second) = Done(await new ReadOwnWrites("own").Send(Cancellation));

            read.Should().Be("own write");
            count.Should().Be(2);
            second.Should().Be(3, "the second number in one command follows the first, saved or not");
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_sequence_needs_a_unique_column_refuses_outside_a_hold_and_inside_a_plain_hold_numbers_as_a_command_does()
    {
        RequireBackingStore();
        var path = TempDatabase("plain-hold");
        try
        {
            await using var host = await Start(Settings(path));

            var unindexed = () => Sequence.Of<CommandSlip>(slip => slip.Ordinal);
            unindexed.Should().Throw<InvalidOperationException>().WithMessage("*Index(Unique = true)*");

            var outside = () => CommandSlip.Numbers.Next(Cancellation);
            (await outside.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("inside a hold");
            using (EntityContext.Transaction("deferred"))
                (await outside.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("inside a hold");

            using (await EntityContext.Hold("plain"))
            {
                await new CommandSlip { Number = await CommandSlip.Numbers.Next(Cancellation), Body = "plain" }.Save(Cancellation);
                await EntityContext.Commit(Cancellation);
            }
            (await CommandSlip.Numbers.Highest(Cancellation)).Should().Be(1);
            Done(await new CreateSlip("after", "s", 0, "body").Send(Cancellation)).Number.Should().Be(2);
        }
        finally { Delete(path); }
    }

    /// <summary>A hold in each process; neither process keeps a number, so each reads what the other committed. This process
    /// sends until the other has finished, so the two are known to interleave.</summary>
    [Fact]
    public async Task Sequences_have_no_gap_across_two_real_processes_and_a_number_a_command_gave_back()
    {
        RequireBackingStore();
        var path = TempDatabase("two-processes");
        try
        {
            await using var host = await Start(Settings(path));
            const int there = 25;

            await using var other = HeldContender.Process.Start("command", path, there.ToString());
            await other.Holding();
            var here = 0;
            Done(await new NumberASlip("here", here++).Send(Cancellation));
            await other.Say("go");
            var finished = other.Finished();
            while (!finished.IsCompleted)
            {
                // One in three gives its number back, so a refused command sits between the two processes' commits too.
                if (here % 3 == 0) (await new SaveThenAnswer($"given-back-{here}", new Outcome<string>.Refused(Reason.Gated("no"))).Send(Cancellation)).Should().BeOfType<Outcome<string>.Refused>();
                Done(await new NumberASlip("here", here++).Send(Cancellation));
            }
            await finished;
            Done(await new NumberASlip("here", here++).Send(Cancellation));
            Output.WriteLine(other.Output);

            var total = here + there;
            var slips = await CommandSlip.FirstPage(total + 1, sort => sort.OrderBy(slip => slip.Number), Cancellation);
            slips.Select(slip => slip.Number).Should().Equal(Enumerable.Range(1, total).Select(number => (long)number));
            slips.Count(slip => slip.Sender == "there").Should().Be(there, "the other process's commands all landed");
            var handOvers = slips.Zip(slips.Skip(1)).Count(pair => pair.First.Sender != pair.Second.Sender);
            Output.WriteLine($"{here} here and {there} there, handing over {handOvers} times");
            handOvers.Should().BeGreaterThanOrEqualTo(2, "this process committed before and after the other");
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_send_waits_for_its_turn_and_a_cancelled_wait_never_runs()
    {
        RequireBackingStore();
        var path = TempDatabase("back-pressure");
        try
        {
            await using var host = await Start(Settings(path));
            var holding = new HoldTheTurn();
            var held = holding.Send(Cancellation);
            await holding.Running.Task.WaitAsync(Bound, Cancellation);

            var waiting = new NumberASlip("waited", 0).Send(Cancellation);
            (await Task.WhenAny(waiting, Task.Delay(200, Cancellation))).Should().NotBe(waiting, "a send waits for its turn");

            using var gaveUp = new CancellationTokenSource();
            var gaveUpSending = new NumberASlip("gave up", 0).Send(gaveUp.Token);
            gaveUp.Cancel();
            var gaveUpFailure = (await gaveUpSending.WaitAsync(Bound, Cancellation)).Should().BeOfType<Outcome<long>.Failed>().Which;
            gaveUpFailure.Landed.Should().Be(DataCommitOutcome.NotDispatched, "its hold never began, so it never ran");
            gaveUpFailure.Cause.Should().BeAssignableTo<OperationCanceledException>();

            holding.Release.SetResult();
            Done(await held.WaitAsync(Bound, Cancellation));
            Done(await waiting.WaitAsync(Bound, Cancellation));

            (await CommandSlip.Query(slip => slip.Sender == "gave up", Cancellation)).Should().BeEmpty();
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_send_behind_a_turn_that_outlasts_the_busy_timeout_is_told_it_never_ran()
    {
        RequireBackingStore();
        var path = TempDatabase("the-bound");
        try
        {
            await using var host = await Start(Settings(path, busyTimeout: TimeSpan.FromSeconds(1)));
            var holding = new HoldTheTurn();
            var held = holding.Send(Cancellation);
            await holding.Running.Task.WaitAsync(Bound, Cancellation);

            var behind = await Task.WhenAll(Enumerable.Range(0, 20).Select(ordinal => new NumberASlip("behind", ordinal).Send(Cancellation))).WaitAsync(Bound, Cancellation);

            behind.Should().AllSatisfy(outcome => outcome.Should().BeOfType<Outcome<long>.Failed>().Which.Landed.Should().Be(DataCommitOutcome.NotDispatched));
            holding.Release.SetResult();
            Done(await held.WaitAsync(Bound, Cancellation));
            (await CommandSlip.Count.Exact(Cancellation)).Should().Be(0);
            Done(await new NumberASlip("after", 0).Send(Cancellation)).Should().Be(1);
        }
        finally { Delete(path); }
    }

    /// <summary>Contention in one process: every one of a burst answered, and one unbroken sequence.</summary>
    [Fact]
    public async Task A_burst_of_concurrent_sends_is_all_answered_with_no_gap()
    {
        RequireBackingStore();
        var path = TempDatabase("burst");
        try
        {
            await using var host = await Start(Settings(path));
            const int burst = 500;

            var clock = Stopwatch.StartNew();
            var answers = await Task.WhenAll(Enumerable.Range(0, burst).Select(ordinal => Task.Run(() => new NumberASlip("burst", ordinal).Send(Cancellation), Cancellation))).WaitAsync(TimeSpan.FromMinutes(2), Cancellation);
            Output.WriteLine($"{burst} concurrent sends: {clock.Elapsed.TotalMilliseconds / burst:F2} ms/command");

            answers.Select(Done).Order().Should().Equal(Enumerable.Range(1, burst).Select(number => (long)number));
            var stored = await CommandSlip.FirstPage(burst + 1, sort => sort.OrderBy(slip => slip.Number), Cancellation);
            stored.Select(slip => slip.Number).Should().Equal(Enumerable.Range(1, burst).Select(number => (long)number));
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_sender_cancelled_while_its_command_runs_is_told_nothing_landed_and_gets_its_number_back()
    {
        RequireBackingStore();
        var path = TempDatabase("cancelled");
        try
        {
            await using var host = await Start(Settings(path));
            using var gaveUp = new CancellationTokenSource();
            var running = new HoldTheTurn("cancelled");
            var sent = running.Send(gaveUp.Token);
            await running.Running.Task.WaitAsync(Bound, Cancellation);

            gaveUp.Cancel();
            var failure = (await sent.WaitAsync(Bound, Cancellation)).Should().BeOfType<Outcome<bool>.Failed>().Which;

            failure.Landed.Should().Be(DataCommitOutcome.NotCommitted);
            failure.Cause.Should().BeAssignableTo<OperationCanceledException>();
            (await CommandSlip.Get("cancelled", Cancellation)).Should().BeNull();
            Done(await new CreateSlip("next", "s", 1, "body").Send(Cancellation)).Number.Should().Be(1);
        }
        finally { Delete(path); }
    }

    /// <summary>The host's first lexical measurement provisions its tables (this assembly declares lexical entities), which needs the write
    /// lock, and the driver's busy wait cannot observe a token. So a stop that overtakes it behind an open hold waits for the hold: that is
    /// <see cref="A_stop_that_overtakes_a_first_measurement_behind_a_hold_waits_no_longer_than_the_hold_can_last"/>. Taken first, as a first use
    /// belongs at boot, it leaves nothing for a stop to wait for.</summary>
    private static Task FirstMeasurement(IntegrationHost host) =>
        host.Services.GetServices<IHealthContributor>().Single(contributor => contributor.Name == "data:lexical").Check(Cancellation);

    [Fact]
    public async Task A_host_that_ends_rolls_back_the_command_that_is_running()
    {
        RequireBackingStore();
        var path = TempDatabase("stop");
        try
        {
            var host = await Start(Settings(path));
            await FirstMeasurement(host);
            using var app = host.Enter();
            var holding = new HoldTheTurn("in-flight");
            var held = holding.Send(Cancellation);
            await holding.Running.Task.WaitAsync(Bound, Cancellation);

            var clock = Stopwatch.StartNew();
            await host.DisposeAsync().AsTask().WaitAsync(Bound, Cancellation);
            Output.WriteLine($"ended in {clock.ElapsedMilliseconds}ms");
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));

            holding.Release.SetResult();
            (await held.WaitAsync(Bound, Cancellation)).Should().BeOfType<Outcome<bool>.Failed>().Which.Landed.Should().Be(DataCommitOutcome.NotCommitted);
            (await CommittedCount(path, Slips)).Should().Be(0);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_stop_that_overtakes_a_first_measurement_behind_a_hold_waits_no_longer_than_the_hold_can_last()
    {
        RequireBackingStore();
        var path = TempDatabase("stop-bound");
        try
        {
            var settings = Settings(path);
            settings[$"{TransactionOptions.SectionPath}:HoldLimit"] = TimeSpan.FromSeconds(2).ToString();
            var host = await Start(settings);
            using var app = host.Enter();
            var holding = new HoldTheTurn("in-flight");
            var held = holding.Send(Cancellation);
            await holding.Running.Task.WaitAsync(Bound, Cancellation);

            // Whether the measurement is still running here is a race, so this holds either way: at once, or when the limit frees the store.
            var clock = Stopwatch.StartNew();
            await host.DisposeAsync().AsTask().WaitAsync(Bound, Cancellation);
            Output.WriteLine($"ended in {clock.ElapsedMilliseconds}ms");
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));

            holding.Release.SetResult();
            (await held.WaitAsync(Bound, Cancellation)).Should().BeOfType<Outcome<bool>.Failed>().Which.Landed.Should().Be(DataCommitOutcome.NotCommitted);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_commit_the_store_refuses_reports_unknown_and_the_next_command_reads_the_store_again()
    {
        RequireBackingStore();
        var path = TempDatabase("unknown");
        try
        {
            await using var host = await Start(Settings(path, ";Foreign Keys=True"));
            await using (var ddl = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await ddl.OpenAsync(Cancellation);
                await using var create = ddl.CreateCommand();
                create.CommandText =
                    "CREATE TABLE koan_command_parent (id TEXT PRIMARY KEY);" +
                    "CREATE TABLE koan_command_child (parent TEXT REFERENCES koan_command_parent (id) DEFERRABLE INITIALLY DEFERRED);";
                await create.ExecuteNonQueryAsync(Cancellation);
            }

            var failure = (await new CommitTheStoreRefuses("refused").Send(Cancellation)).Should().BeOfType<Outcome<bool>.Failed>().Which;

            failure.Landed.Should().Be(DataCommitOutcome.Unknown);
            failure.Cause.Should().BeOfType<TransactionException>();
            (await CommandSlip.Get("refused", Cancellation)).Should().BeNull();
            Done(await new CreateSlip("after", "s", 0, "body").Send(Cancellation)).Number.Should().Be(1, "nothing to reload: the next hold reads what is there");
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_hold_that_cannot_begin_behind_another_writer_never_ran_and_the_next_send_goes_on()
    {
        RequireBackingStore();
        var path = TempDatabase("busy");
        try
        {
            await using var host = await Start(Settings(path, busyTimeout: TimeSpan.FromSeconds(1)));
            Done(await new CreateSlip("first", "s", 0, "body").Send(Cancellation)).Number.Should().Be(1);

            Outcome<CommandSlip> blocked;
            await using (var holder = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await holder.OpenAsync(Cancellation);
                await using var held = holder.BeginTransaction(deferred: false);
                blocked = await new CreateSlip("blocked", "s", 1, "body").Send(Cancellation);
            }

            var failure = blocked.Should().BeOfType<Outcome<CommandSlip>.Failed>().Which;
            failure.Landed.Should().Be(DataCommitOutcome.NotDispatched, "its hold never began, so it never ran");
            failure.Cause.Should().BeOfType<TransactionException>();
            (await CommandSlip.Get("blocked", Cancellation)).Should().BeNull();
            Done(await new CreateSlip("after", "s", 2, "body").Send(Cancellation)).Number.Should().Be(2);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task Work_following_a_commit_can_send_and_await_it_since_the_turn_has_passed_on()
    {
        RequireBackingStore();
        var path = TempDatabase("re-entry");
        try
        {
            await using var host = await Start(Settings(path));
            var command = new SendAfterItsCommit("after");

            Done(await command.Send(Cancellation).WaitAsync(Bound, Cancellation));

            Done(await command.Answered.Task.WaitAsync(Bound, Cancellation)).Number.Should().Be(2);
            (await CommandSlip.Get("after", Cancellation)).Should().NotBeNull();
            (await CommandSlip.Get("after-next", Cancellation)).Should().NotBeNull();
            Done(await new CreateSlip("later", "s", 0, "body").Send(Cancellation).WaitAsync(Bound, Cancellation)).Number.Should().Be(3);
        }
        finally { Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_command_that_never_returns_hangs_only_its_own_sender_and_the_limit_frees_the_store(bool blocks)
    {
        RequireBackingStore();
        var path = TempDatabase("limit");
        try
        {
            var settings = Settings(path);
            settings[$"{TransactionOptions.SectionPath}:HoldLimit"] = TimeSpan.FromSeconds(1).ToString();
            await using var host = await Start(settings);
            var stuck = new IgnoresItsToken("stuck", blocks);

            var sent = Task.Run(() => stuck.Send(Cancellation), Cancellation);
            await stuck.Running.Task.WaitAsync(Bound, Cancellation);

            Done(await new CreateSlip("next", "s", 0, "body").Send(Cancellation).WaitAsync(Bound, Cancellation)).Number.Should().Be(1, "the limit ended the hold, and gave back what it took");
            sent.IsCompleted.Should().BeFalse("it hangs its own sender, and nobody else");

            // Let go long after its hold ended, it finds nothing to write into, and is told nothing landed.
            stuck.Release.SetResult();
            (await stuck.LateWrite.Task.WaitAsync(Bound, Cancellation)).Should().NotBeNull("an ended hold refuses a late write");
            (await sent.WaitAsync(Bound, Cancellation)).Should().BeOfType<Outcome<bool>.Failed>().Which.Landed.Should().Be(DataCommitOutcome.NotCommitted);
            (await CommandSlip.Get("stuck", Cancellation)).Should().BeNull();
            (await CommandSlip.Get("stuck-late", Cancellation)).Should().BeNull();
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task Work_after_a_commit_that_never_ends_hangs_only_its_own_sender()
    {
        RequireBackingStore();
        var path = TempDatabase("hang-after");
        try
        {
            await using var host = await Start(Settings(path));

            var sent = new HangAfterItsCommit("hangs").Send(Cancellation);

            Done(await new CreateSlip("next", "s", 0, "body").Send(Cancellation).WaitAsync(Bound, Cancellation)).Number.Should().Be(2, "the commit stands, and the turn had passed on");
            sent.IsCompleted.Should().BeFalse();
            (await CommandSlip.Get("hangs", Cancellation)).Should().NotBeNull();
        }
        finally { Delete(path); }
    }

    public static TheoryData<string> EndsOfAHold => ["Commit", "Rollback", "CoordinatorCommit", "CoordinatorRollback"];

    [Theory]
    [MemberData(nameof(EndsOfAHold))]
    public async Task A_command_cannot_end_its_own_hold(string how)
    {
        RequireBackingStore();
        var path = TempDatabase("own-end");
        try
        {
            await using var host = await Start(Settings(path));
            Func<Task> end = how switch
            {
                "Commit" => () => EntityContext.Commit(),
                "Rollback" => () => EntityContext.Rollback(),
                "CoordinatorCommit" => () => EntityContext.CurrentTransaction!.Commit(),
                _ => () => EntityContext.CurrentTransaction!.Rollback(),
            };

            var failure = (await new EndItsOwnHold("own", end, new Outcome<bool>.Done(true)).Send(Cancellation)).Should().BeOfType<Outcome<bool>.Failed>().Which;

            failure.Landed.Should().Be(DataCommitOutcome.NotCommitted);
            failure.Cause.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("does not end its own hold");
            (await CommandSlip.Get("own", Cancellation)).Should().BeNull("the row went back with the refused end");
            Done(await new CreateSlip("next", "s", 0, "body").Send(Cancellation)).Number.Should().Be(1);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_command_runs_in_its_senders_partition_and_context_and_answers_there()
    {
        RequireBackingStore();
        var path = TempDatabase("sender-context");
        try
        {
            var host = await KoanIntegrationHost.Configure()
                .WithSettings(Settings(path))
                .ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.AddSingleton<IKoanContextCarrier, CarriedValue>();
                })
                .StartAsync(Cancellation);
            await using (host)
            {
                Outcome<(string? Partition, string? Carried)> outcome;
                using (EntityContext.Partition("tenant-a"))
                using (CarriedValue.Set("from the sender"))
                    outcome = await new WhereAmI("there").Send(Cancellation);

                Done(outcome).Should().Be(("tenant-a", "from the sender"));
                (await CommandSlip.Get("there", Cancellation)).Should().BeNull("nothing landed in the root partition");
                using (EntityContext.Partition("tenant-a"))
                    (await CommandSlip.Get("there", Cancellation)).Should().NotBeNull("it landed where its sender is");
                var unscoped = await new WhereAmI("root").Send(Cancellation);
                Done(unscoped).Should().Be((null, null), "the next command does not inherit the last one's context");
            }
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_startup_service_that_sends_with_no_token_is_answered()
    {
        RequireBackingStore();
        var path = TempDatabase("seed");
        try
        {
            var seed = new SeedAtStartup<CreateSlip>(() => new CreateSlip("seed", "s", 0, "body").Send());
            var host = await KoanIntegrationHost.Configure()
                .WithSettings(Settings(path))
                .ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.AddSingleton<IKoanBackgroundService>(seed);
                })
                .StartAsync(Cancellation);
            await using (host)
                Done(await seed.Answered.Task.WaitAsync(Bound, Cancellation)).Number.Should().Be(1);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_send_is_answered_with_background_services_turned_off()
    {
        RequireBackingStore();
        var path = TempDatabase("services-off");
        try
        {
            var settings = Settings(path);
            settings["Koan:BackgroundServices:Enabled"] = "false";
            await using var host = await Start(settings);

            Done(await new CreateSlip("off", "s", 0, "body").Send().WaitAsync(Bound, Cancellation)).Number.Should().Be(1);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task Two_sequences_in_one_command_count_apart_and_go_back_together()
    {
        RequireBackingStore();
        var path = TempDatabase("two-sequences");
        try
        {
            await using var host = await Start(Settings(path));
            await new CommandTicket { Id = "first", Serial = 0 }.Save(Cancellation);
            await CommandTicket.Remove("first", Cancellation);
            await new CommandTicket { Serial = 40 }.Save(Cancellation);

            Done(await new NumberTwoThings().Send(Cancellation)).Should().Be((1L, 41L));
            (await new NumberTwoThings(new Outcome<(long, long)>.Refused(Reason.Gated("no"))).Send(Cancellation)).Should().BeOfType<Outcome<(long, long)>.Refused>();
            Done(await new NumberTwoThings().Send(Cancellation)).Should().Be((2L, 42L), "both numbers the refused command took were given back");
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_send_from_inside_a_scope_is_told_it_never_ran_rather_than_waiting_for_its_own_turn()
    {
        RequireBackingStore();
        var path = TempDatabase("nested");
        try
        {
            await using var host = await Start(Settings(path));

            var inner = Done(await new SendFromInside().Send(Cancellation).WaitAsync(Bound, Cancellation));
            var fromCommand = inner.Should().BeOfType<Outcome<CommandSlip>.Failed>().Which;
            fromCommand.Landed.Should().Be(DataCommitOutcome.NotDispatched);
            fromCommand.Cause.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("nested");

            using (await EntityContext.Hold("mine"))
            {
                var fromHold = (await new CreateSlip("in-hold", "s", 0, "body").Send(Cancellation)).Should().BeOfType<Outcome<CommandSlip>.Failed>().Which;
                fromHold.Landed.Should().Be(DataCommitOutcome.NotDispatched);
                fromHold.Cause.Message.Should().Contain("'mine'");
            }

            using (EntityContext.Transaction("deferred"))
                (await new CreateSlip("in-scope", "s", 0, "body").Send(Cancellation)).Should().BeOfType<Outcome<CommandSlip>.Failed>().Which.Cause.Message.Should().Contain("'deferred'");

            (await CommandSlip.Get("in-hold", Cancellation)).Should().BeNull();
            Done(await new CreateSlip("after", "s", 0, "body").Send(Cancellation)).Number.Should().Be(1);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_refused_first_command_on_a_fresh_file_leaves_the_next_command_a_table_to_write_to()
    {
        RequireBackingStore();
        var path = TempDatabase("fresh");
        try
        {
            await using var host = await Start(Settings(path), provision: false);

            (await new SaveThenAnswer("gated", new Outcome<string>.Refused(Reason.Gated("no"))).Send(Cancellation)).Should().BeOfType<Outcome<string>.Refused>();
            Done(await new CreateSlip("after", "s", 0, "body").Send(Cancellation)).Number.Should().Be(1);
            (await CommandSlip.Get("after", Cancellation)).Should().NotBeNull();
        }
        finally { Delete(path); }
    }

    /// <summary>The cost of a number against a store that already holds thousands, which is why no mark is kept in memory (DATA-0167).
    /// Both sides are measured here, so a slow machine slows both: a read that scanned the table would cost more than a whole command.</summary>
    [Fact]
    public async Task A_number_read_from_the_store_costs_a_small_part_of_a_command()
    {
        RequireBackingStore();
        var path = TempDatabase("cost");
        try
        {
            await using var host = await Start(Settings(path));
            await Enumerable.Range(1, 20_000).Select(number => new CommandSlip { Number = number, Body = "seed" }).ToList().Save(Cancellation);

            const int rounds = 100;
            var clock = Stopwatch.StartNew();
            for (var round = 0; round < rounds; round++) Done(await new SaveAStub().Send(Cancellation));
            var unnumbered = clock.Elapsed;
            clock.Restart();
            for (var round = 0; round < rounds; round++) Done(await new NumberASlip("cost", round).Send(Cancellation));
            var numbered = clock.Elapsed;
            clock.Restart();
            using (await EntityContext.Hold("read only"))
                for (var round = 0; round < rounds; round++) await CommandSlip.Numbers.Highest(Cancellation);
            var read = clock.Elapsed;

            Output.WriteLine($"over 20000 rows: unnumbered {unnumbered.TotalMilliseconds / rounds:F2} ms/command, numbered {numbered.TotalMilliseconds / rounds:F2} ms/command, " +
                             $"the read alone {read.TotalMilliseconds / rounds:F3} ms");
            read.Should().BeLessThan(unnumbered / 4, "the read is one row, however many the table holds, and is a small part of a command");
            (await CommandSlip.Numbers.Highest(Cancellation)).Should().Be(20_000 + rounds);
        }
        finally { Delete(path); }
    }
}
