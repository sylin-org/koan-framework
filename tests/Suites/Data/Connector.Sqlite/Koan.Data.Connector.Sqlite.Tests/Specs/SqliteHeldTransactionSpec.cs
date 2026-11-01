using System.Diagnostics;
using System.IO;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Direct;
using Koan.Data.Core.Lifecycle;
using Koan.Data.Core.Transactions;
using Koan.Data.Relational;
using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using KoanData = Koan.Data.Core.Data;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// DATA-0166: <c>EntityContext.Hold</c> holds one native transaction from the start of its scope, across awaited
/// application code. Every read and write of its route from the flow runs inside it, a write acts when it is issued, and
/// a second writer waits for the first, in another process too, for at most the busy timeout. It ends at commit or
/// rollback, at its limit, and when the host stops.
/// </summary>
public sealed class SqliteHeldTransactionSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Storage(Name = "KOAN_HOLD_TICKET")]
    public sealed class Ticket : Entity<Ticket>
    {
        public string Title { get; set; } = "";
        public bool Vetoed { get; set; }
    }

    [Storage(Name = "KOAN_HOLD_AUDIT")]
    public sealed class Audit : Entity<Audit>
    {
        public string TicketId { get; set; } = "";
    }

    [Storage(Name = "KOAN_HOLD_CLAIM")]
    public sealed class Claim : Entity<Claim>
    {
        [Index(Name = "ix_hold_claim_code", Unique = true)] public string Code { get; set; } = "";
    }

    private static void AuditTickets()
    {
        Ticket.Lifecycle.BeforeUpsert(context => context.Current.Vetoed
            ? EntityLifecycleResult.Cancel("vetoed")
            : EntityLifecycleResult.Proceed());
        Ticket.Lifecycle.AfterUpsert(async context => await new Audit { TicketId = context.Current.Id }.Save());
    }

    private static string TempDatabase(string label) =>
        Path.Combine(Path.GetTempPath(), $"koan-sqlite-hold-{label}-{Guid.CreateVersion7():n}.db");

    private static Dictionary<string, string?> Settings(string path, TimeSpan? busyTimeout = null, TimeSpan? holdLimit = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path}",
        };
        if (busyTimeout is { } busy) settings["Koan:Data:Sqlite:BusyTimeout"] = busy.ToString();
        if (holdLimit is { } limit) settings[$"{TransactionOptions.SectionPath}:HoldLimit"] = limit.ToString();
        return settings;
    }

    private static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }

    /// <summary>What a connection outside the hold reads, with the same bound any writer has.</summary>
    private static async Task<long> CommittedCount(string path, string table)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<bool> WriteLockIsFree(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False;Default Timeout=1");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 1;
        command.CommandText = "BEGIN IMMEDIATE";
        try
        {
            await command.ExecuteNonQueryAsync();
            command.CommandText = "ROLLBACK";
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (SqliteException error) when (error.SqliteErrorCode == 5)
        {
            return false;
        }
    }

    [Fact]
    public async Task A_read_inside_a_hold_sees_its_own_write_and_nothing_outside_does_until_it_commits()
    {
        RequireBackingStore();
        var path = TempDatabase("own-writes");
        try
        {
            await using var host = await BootAsync(Settings(path));
            await new Ticket { Title = "committed before" }.Save();
            var ticket = new Ticket { Title = "mine" };

            using (await EntityContext.Hold("reply"))
            {
                await ticket.Save();
                (await Ticket.Get(ticket.Id))!.Title.Should().Be("mine");
                (await Ticket.Query(item => item.Title == "mine")).Should().ContainSingle();
                (await Data<Ticket, string>.Count()).Should().Be(2);
                (await CommittedCount(path, Tickets)).Should().Be(1, "another connection reads committed state");
                await EntityContext.Commit();
            }

            (await CommittedCount(path, Tickets)).Should().Be(2);
            (await Ticket.Get(ticket.Id))!.Title.Should().Be("mine");
        }
        finally
        {
            Delete(path);
        }
    }

    private const string Tickets = "KOAN_HOLD_TICKET";
    private const string Notes = "KOAN_HOLD_NOTE";

    [Fact]
    public async Task Disposing_a_hold_without_a_commit_or_throwing_inside_it_leaves_nothing()
    {
        RequireBackingStore();
        var path = TempDatabase("rollback");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var forgotten = new Ticket { Title = "forgotten" };
            var thrown = new Ticket { Title = "thrown" };

            using (await EntityContext.Hold("forgets")) await forgotten.Save();

            var work = async () =>
            {
                using (await EntityContext.Hold("throws"))
                {
                    await thrown.Save();
                    throw new InvalidOperationException("between the writes");
                }
            };
            await work.Should().ThrowAsync<InvalidOperationException>().WithMessage("between the writes");

            (await Ticket.Get(forgotten.Id)).Should().BeNull();
            (await Ticket.Get(thrown.Id)).Should().BeNull();
            (await WriteLockIsFree(path)).Should().BeTrue();
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task Every_write_a_deferred_scope_refuses_joins_a_hold_and_rolls_back_with_it()
    {
        RequireBackingStore();
        var path = TempDatabase("every-write");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var kept = await new Ticket { Title = "kept" }.Save();
            var patched = await new Ticket { Title = "patched" }.Save();

            using (await EntityContext.Hold("all-of-it"))
            {
                // A batch, a raw statement, a conditional write and a mass delete, each of which a deferred scope refuses.
                await Data<Ticket, string>.Batch().Add(new Ticket { Title = "batched" }).Save();
                (await Data<Ticket, string>.Count()).Should().Be(3);
                kept.Title = "replaced";
                (await Data<Ticket, string>.As<IConditionalWriteRepository<Ticket, string>>()!
                    .ConditionalReplaceAsync(kept, Filter.Eq(nameof(Ticket.Title), "kept"))).Should().BeTrue();
                (await Ticket.Get(kept.Id))!.Title.Should().Be("replaced");
                (await Data<Ticket, string>.As<IConditionalDeleteRepository<Ticket, string>>()!
                    .ConditionalDeleteAsync(kept.Id, Filter.Eq(nameof(Ticket.Title), "replaced"))).Should().BeTrue();
                (await Data<Ticket, string>.Execute<int>(InstructionSql.NonQuery(
                    $"DELETE FROM \"{Tickets}\" WHERE rowid IN (SELECT rowid FROM \"{Tickets}\" LIMIT 1)",
                    DataOperationEffect.Write))).Should().Be(1);
                (await Data<Ticket, string>.DeleteAll()).Should().Be(1);
                (await Data<Ticket, string>.Count()).Should().Be(0);
                await EntityContext.Rollback();
            }

            (await Ticket.Get(kept.Id))!.Title.Should().Be("kept");
            (await Ticket.Get(patched.Id))!.Title.Should().Be("patched");
            (await Data<Ticket, string>.Count()).Should().Be(2);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_save_reports_its_outcome_as_it_happens_and_not_as_committed()
    {
        RequireBackingStore();
        var path = TempDatabase("outcomes");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var ticket = new Ticket { Title = "first" };

            using (await EntityContext.Hold("outcomes"))
            {
                var inserted = await ticket.SaveWithOutcome();
                inserted.Outcome.Should().Be(MutationOutcome.Inserted);
                inserted.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted, "only the scope's commit commits");

                ticket.Title = "second";
                (await ticket.SaveWithOutcome()).Outcome.Should().Be(MutationOutcome.Updated);

                var removed = await ticket.RemoveWithOutcome();
                removed.Outcome.Should().Be(MutationOutcome.Deleted);
                removed.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted);
                (await ticket.RemoveWithOutcome()).Outcome.Should().Be(MutationOutcome.Missing);

                var inserts = await new Ticket { Title = "inserted" }.Insert();
                inserts.Outcome.Should().Be(MutationOutcome.Inserted);
                inserts.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted);
                (await new Ticket { Id = inserts.Key, Title = "again" }.Insert()).Outcome.Should().Be(MutationOutcome.Conflict);
                await EntityContext.Commit();
            }

            (await Data<Ticket, string>.Count()).Should().Be(1, "the insert was committed and the removed row was not");
            var outside = await new Ticket { Title = "outside" }.SaveWithOutcome();
            outside.Outcome.Should().Be(MutationOutcome.Inserted);
            outside.CommitOutcome.Should().Be(DataCommitOutcome.Committed, "outside a hold the save is its own commit");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_unique_index_answers_at_the_save_and_the_hold_goes_on()
    {
        RequireBackingStore();
        var path = TempDatabase("unique");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var first = new Claim { Code = "taken" };

            using (await EntityContext.Hold("claims"))
            {
                await first.Save();
                var duplicate = () => new Claim { Code = "taken" }.Save();
                await duplicate.Should().ThrowAsync<UniqueValueConflictException>();
                await new Claim { Code = "free" }.Save();
                await EntityContext.Commit();
            }

            (await Claim.Get(first.Id)).Should().NotBeNull("the conflict cost the statement, not the transaction");
            (await Data<Claim, string>.Count()).Should().Be(2);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_lifecycle_handler_acts_at_the_save_and_what_it_writes_rolls_back_with_the_hold()
    {
        RequireBackingStore();
        var path = TempDatabase("lifecycle");
        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSettings(Settings(path))
                .ConfigureServices(services => services.AddKoan(AuditTickets))
                .StartAsync(TestContext.Current.CancellationToken);
            var ticket = new Ticket { Title = "audited" };

            using (await EntityContext.Hold("audited"))
            {
                await ticket.Save();
                (await Audit.Query(item => item.TicketId == ticket.Id)).Should().ContainSingle("the handler ran at the save, inside the hold");
                var vetoed = () => new Ticket { Vetoed = true }.Save();
                await vetoed.Should().ThrowAsync<Exception>("the handler's veto reaches the caller at the save");
                await EntityContext.Rollback();
            }

            (await Audit.Query(item => item.TicketId == ticket.Id)).Should().BeEmpty();
            (await Ticket.Get(ticket.Id)).Should().BeNull();
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_raw_read_sees_the_holds_own_write_and_still_cannot_write()
    {
        RequireBackingStore();
        var path = TempDatabase("raw");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var ticket = new Ticket { Title = "raw" };

            using (await EntityContext.Hold("raw"))
            {
                await ticket.Save();
                (await Data<Ticket, string>.QueryRaw("1 = 1")).Should().ContainSingle("a raw read runs on the hold's connection");

                var mislabelled = () => Data<Ticket, string>.Execute<int>(InstructionSql.NonQuery(
                    $"DELETE FROM \"{Tickets}\"", DataOperationEffect.Read));
                await mislabelled.Should().ThrowAsync<SqliteException>("a read's fence holds on the shared connection too");

                await new Ticket { Title = "after the fence" }.Save();
                await EntityContext.Commit();
            }

            (await Data<Ticket, string>.Count()).Should().Be(2, "the fence lasts one read");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_stream_read_inside_a_hold_sees_its_writes_and_the_body_may_write()
    {
        RequireBackingStore();
        var path = TempDatabase("stream");
        try
        {
            await using var host = await BootAsync(Settings(path));
            using (await EntityContext.Hold("stream"))
            {
                for (var i = 0; i < 5; i++) await new Ticket { Title = "row" }.Save();
                var seen = 0;
                await foreach (var ticket in Data<Ticket, string>.AllStream(batchSize: 2))
                {
                    seen++;
                    await new Audit { TicketId = ticket.Id }.Save();
                }
                seen.Should().Be(5);
                await EntityContext.Commit();
            }
            (await Data<Audit, string>.Count()).Should().Be(5);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task Operations_started_at_once_inside_a_hold_take_turns_on_its_one_connection()
    {
        RequireBackingStore();
        var path = TempDatabase("turns");
        try
        {
            await using var host = await BootAsync(Settings(path));
            using (await EntityContext.Hold("concurrent"))
            {
                await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(async () =>
                {
                    var ticket = await new Ticket { Title = "concurrent" }.Save();
                    (await Ticket.Get(ticket.Id)).Should().NotBeNull();
                })));
                await EntityContext.Commit();
            }
            (await CommittedCount(path, Tickets)).Should().Be(40);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_read_lane_keeps_its_own_connection_sees_committed_state_and_is_not_blocked()
    {
        RequireBackingStore();
        var path = TempDatabase("lane");
        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSettings(new Dictionary<string, string?>(Settings(path), StringComparer.Ordinal)
                {
                    ["Koan:Data:Sources:Default:ReadLanes:Reports:ConnectionString"] = $"Data Source={path}",
                })
                .ConfigureServices(services => services.AddKoan(koan =>
                    koan.Data.Source("Default").Scalar<long>("tickets.count", query => query
                        .Lane("Reports")
                        .Sql($"SELECT COUNT(*) FROM \"{Tickets}\""))))
                .StartAsync(TestContext.Current.CancellationToken);
            await new Ticket { Title = "committed" }.Save();

            using (await EntityContext.Hold("lane"))
            {
                await new Ticket { Title = "not yet" }.Save();
                (await Data<Ticket, string>.Count()).Should().Be(2);
                (await KoanData.Source("Default").Scalar<long>("tickets.count")).Should().Be(1, "a lane is a connection of its own");
                await EntityContext.Rollback();
            }
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_direct_write_inside_a_hold_refuses_at_once_and_a_direct_read_sees_committed_state()
    {
        RequireBackingStore();
        var path = TempDatabase("direct");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var data = host.Services.GetRequiredService<IDataService>();
            await new Ticket { Title = "table exists" }.Save();

            using (await EntityContext.Hold("direct"))
            {
                await new Ticket { Title = "held" }.Save();
                var clock = Stopwatch.StartNew();
                var write = () => data.Direct(source: "Default").Effect(DataOperationEffect.Write)
                    .Execute($"DELETE FROM \"{Tickets}\"");
                (await write.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("cannot join");
                var begin = () => data.Direct(source: "Default").Effect(DataOperationEffect.Write).Begin();
                begin.Should().Throw<NotSupportedException>();
                clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "it does not wait on the lock its own flow holds");
                (await data.Direct(source: "Default").Effect(DataOperationEffect.Read)
                    .Scalar<long>($"SELECT COUNT(*) FROM \"{Tickets}\"")).Should().Be(1);
                await EntityContext.Commit();
            }
            (await CommittedCount(path, Tickets)).Should().Be(2);
            (await data.Direct(source: "Default").Effect(DataOperationEffect.Write)
                .Execute($"DELETE FROM \"{Tickets}\"")).Should().Be(2, "outside a hold it runs as it always did");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_second_hold_in_this_process_waits_for_the_first_and_reads_what_it_committed()
    {
        RequireBackingStore();
        var path = TempDatabase("second-hold");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var counter = await new HeldCounter { Value = 0 }.Save();
            var firstHolds = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var first = Task.Run(async () =>
            {
                using (await EntityContext.Hold("first"))
                {
                    var current = await HeldCounter.Get(counter.Id);
                    firstHolds.SetResult();
                    await release.Task;
                    current!.Value = 7;
                    await current.Save();
                    await EntityContext.Commit();
                }
            });
            await firstHolds.Task;

            var second = Task.Run(async () =>
            {
                using (await EntityContext.Hold("second"))
                {
                    var current = await HeldCounter.Get(counter.Id);
                    current!.Value.Should().Be(7, "the second hold begins after the first has committed");
                    current.Value = 8;
                    await current.Save();
                    await EntityContext.Commit();
                }
            });

            (await Task.WhenAny(second, Task.Delay(500))).Should().NotBe(second, "the second hold waits");
            release.SetResult();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(60));
            (await HeldCounter.Get(counter.Id))!.Value.Should().Be(8);
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary>A waiter costs no thread: many holds queue for one database while each holder needs the pool to continue.</summary>
    [Fact]
    public async Task Many_holds_in_one_process_serialize_without_starving_their_own_continuations()
    {
        RequireBackingStore();
        var path = TempDatabase("many");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var counter = await new HeldCounter { Value = 0 }.Save();
            var contenders = Environment.ProcessorCount * 8;

            await Task.WhenAll(Enumerable.Range(0, contenders).Select(_ => Task.Run(async () =>
            {
                using (await EntityContext.Hold("many"))
                {
                    var current = await HeldCounter.Get(counter.Id);
                    await Task.Delay(5);
                    current!.Value++;
                    await current.Save();
                    await EntityContext.Commit();
                }
            }))).WaitAsync(TimeSpan.FromSeconds(120));

            (await HeldCounter.Get(counter.Id))!.Value.Should().Be(contenders);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_writer_that_is_not_a_hold_waits_for_the_hold_too()
    {
        RequireBackingStore();
        var path = TempDatabase("plain-writer");
        try
        {
            await using var host = await BootAsync(Settings(path));
            await new Ticket { Title = "table exists" }.Save();
            var plain = default(Task<Ticket>);

            using (await EntityContext.Hold("first"))
            {
                await new Ticket { Title = "held" }.Save();
                // A flow of its own, so it does not inherit the hold.
                using (ExecutionContext.SuppressFlow())
                    plain = Task.Run(() => new Ticket { Title = "plain" }.Save());
                (await Task.WhenAny(plain, Task.Delay(500))).Should().NotBe(plain, "the write lock is held");
                await EntityContext.Commit();
            }

            await plain.WaitAsync(TimeSpan.FromSeconds(60));
            (await CommittedCount(path, Tickets)).Should().Be(3);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_hold_that_waits_past_the_busy_timeout_reports_nothing_landed_and_leaves_nothing_in_the_flow()
    {
        RequireBackingStore();
        var path = TempDatabase("busy");
        try
        {
            await using var host = await BootAsync(Settings(path, busyTimeout: TimeSpan.FromSeconds(1)));
            await new Ticket { Title = "table exists" }.Save();
            await using var holder = new SqliteConnection($"Data Source={path};Pooling=False");
            await holder.OpenAsync();
            await using (var begin = holder.CreateCommand())
            {
                begin.CommandText = "BEGIN IMMEDIATE";
                await begin.ExecuteNonQueryAsync();
            }

            var clock = Stopwatch.StartNew();
            var hold = async () => { using (await EntityContext.Hold("blocked")) await EntityContext.Commit(); };
            var failure = (await hold.Should().ThrowAsync<TransactionException>()).Which;
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
            failure.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted);
            failure.InnerException.Should().BeOfType<SqliteException>().Which.SqliteErrorCode.Should().Be(5);
            EntityContext.InTransaction.Should().BeFalse("a hold that did not begin left nothing ambient");

            await using (var rollback = holder.CreateCommand())
            {
                rollback.CommandText = "ROLLBACK";
                await rollback.ExecuteNonQueryAsync();
            }
            await new Ticket { Title = "the flow goes on" }.Save();
            (await CommittedCount(path, Tickets)).Should().Be(2);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_cancelled_wait_ends_the_wait_and_takes_no_hold()
    {
        RequireBackingStore();
        var path = TempDatabase("cancelled");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = Task.Run(async () =>
            {
                using (await EntityContext.Hold("first"))
                {
                    holding.SetResult();
                    await release.Task;
                }
            });
            await holding.Task;

            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var clock = Stopwatch.StartNew();
            var hold = async () => { using (await EntityContext.Hold("waiting", cancel.Token)) { } };
            await hold.Should().ThrowAsync<OperationCanceledException>();
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the wait observes its token, not the 30-second bound");
            EntityContext.InTransaction.Should().BeFalse();

            release.SetResult();
            await first;
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_hold_past_its_limit_rolls_back_frees_the_store_and_refuses_the_rest_of_its_scope()
    {
        RequireBackingStore();
        var path = TempDatabase("limit");
        try
        {
            await using var host = await BootAsync(Settings(path, holdLimit: TimeSpan.FromSeconds(1)));
            await new Ticket { Title = "table exists" }.Save();
            var ticket = new Ticket { Title = "too slow" };

            using (await EntityContext.Hold("slow"))
            {
                await ticket.Save();
                await Task.Delay(TimeSpan.FromSeconds(2));
                (await WriteLockIsFree(path)).Should().BeTrue("the limit released the store while the scope is still open");

                var write = () => new Ticket { Title = "after the limit" }.Save();
                (await write.Should().ThrowAsync<TransactionException>()).Which.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted);
                var read = () => Ticket.Get(ticket.Id);
                (await read.Should().ThrowAsync<TransactionException>("a read here would be outside the transaction the scope believes it is in"))
                    .Which.Message.Should().Contain("past its limit");
                var commit = () => EntityContext.Commit();
                var failure = (await commit.Should().ThrowAsync<TransactionException>()).Which;
                failure.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted);
                failure.Message.Should().Contain("past its limit");
            }

            (await Ticket.Get(ticket.Id)).Should().BeNull();
            (await CommittedCount(path, Tickets)).Should().Be(1);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task An_abandoned_hold_ends_at_its_limit_and_the_writer_waiting_behind_it_goes_on()
    {
        RequireBackingStore();
        var path = TempDatabase("abandoned");
        try
        {
            await using var host = await BootAsync(Settings(path, holdLimit: TimeSpan.FromSeconds(3)));
            await new Ticket { Title = "table exists" }.Save();
            var lost = new Ticket { Title = "abandoned" };

            await Abandon(lost);
            (await WriteLockIsFree(path)).Should().BeFalse("the abandoned hold still holds the store");

            var clock = Stopwatch.StartNew();
            using (await EntityContext.Hold("behind it"))
            {
                clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
                (await Ticket.Get(lost.Id)).Should().BeNull("the abandoned write rolled back");
                await new Ticket { Title = "goes on" }.Save();
                await EntityContext.Commit();
            }
            (await CommittedCount(path, Tickets)).Should().Be(2);
        }
        finally
        {
            Delete(path);
        }

        static async Task Abandon(Ticket ticket)
        {
            // Never disposed, and this method's return takes the scope out of the caller's ambient.
            _ = await EntityContext.Hold("abandoned");
            await ticket.Save();
        }
    }

    [Fact]
    public async Task A_host_stopping_with_a_hold_open_rolls_it_back_and_leaves_no_lock()
    {
        RequireBackingStore();
        var path = TempDatabase("stop");
        try
        {
            var host = await BootAsync(Settings(path));
            await new Ticket { Title = "kept" }.Save();
            var scope = await EntityContext.Hold("interrupted");
            await new Ticket { Title = "interrupted" }.Save();

            await host.DisposeAsync();

            (await WriteLockIsFree(path)).Should().BeTrue();
            (await CommittedCount(path, Tickets)).Should().Be(1);
            var commit = () => EntityContext.Commit();
            (await commit.Should().ThrowAsync<TransactionException>()).Which.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted);
            scope.Dispose();
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_child_flow_shares_the_hold_while_it_lasts_and_cannot_write_after_it_ends()
    {
        RequireBackingStore();
        var path = TempDatabase("child-flow");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var inside = new Ticket { Title = "inside" };
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? late = null;

            using (await EntityContext.Hold("parent"))
            {
                await Task.Run(() => inside.Save());
                (await Ticket.Get(inside.Id)).Should().NotBeNull("a child flow's write joined the hold");
                late = Task.Run(async () =>
                {
                    await release.Task;
                    await new Ticket { Title = "too late" }.Save();
                });
                await EntityContext.Commit();
            }

            release.SetResult();
            await late.Invoking(task => task).Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been completed*");
            (await CommittedCount(path, Tickets)).Should().Be(1);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task After_commit_a_hold_refuses_a_write_and_answers_a_read_from_committed_state()
    {
        RequireBackingStore();
        var path = TempDatabase("after-commit");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var ticket = new Ticket { Title = "committed" };

            using (await EntityContext.Hold("done"))
            {
                await ticket.Save();
                await EntityContext.Commit();
                await EntityContext.Commit();
                await EntityContext.Rollback();

                var write = () => new Ticket { Title = "after" }.Save();
                await write.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been completed*");
                (await Ticket.Get(ticket.Id)).Should().NotBeNull();
            }
            (await CommittedCount(path, Tickets)).Should().Be(1);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_transfer_still_refuses_inside_a_hold()
    {
        RequireBackingStore();
        var path = TempDatabase("transfer");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var ticket = await new Ticket { Title = "stays" }.Save();
            using (await EntityContext.Hold("transfer"))
            {
                var copy = () => Ticket.Copy().To(partition: "copy-target").Run();
                await copy.Should().ThrowAsync<InvalidOperationException>();
                await EntityContext.Commit();
            }
            using (EntityContext.Partition("copy-target")) (await Ticket.Get(ticket.Id)).Should().BeNull();
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_commit_cancelled_before_it_begins_reports_nothing_landed_and_rolls_back()
    {
        RequireBackingStore();
        var path = TempDatabase("commit-cancelled");
        try
        {
            await using var host = await BootAsync(Settings(path));
            await new Ticket { Title = "table exists" }.Save();
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            using (await EntityContext.Hold("commit cancelled"))
            {
                await new Ticket { Title = "never" }.Save();
                var commit = () => EntityContext.Commit(cancelled.Token);
                var failure = (await commit.Should().ThrowAsync<TransactionException>()).Which;
                failure.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted, "the native commit never began");
                failure.InnerException.Should().BeAssignableTo<OperationCanceledException>();
            }

            (await CommittedCount(path, Tickets)).Should().Be(1);
            (await WriteLockIsFree(path)).Should().BeTrue();
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary>A deferred foreign key is checked by COMMIT itself, so this is the store refusing the native commit.</summary>
    [Fact]
    public async Task A_native_commit_the_store_refuses_reports_unknown_and_rolls_back()
    {
        RequireBackingStore();
        var path = TempDatabase("commit-refused");
        try
        {
            await using var host = await BootAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path};Foreign Keys=True",
            });
            await new Ticket { Title = "table exists" }.Save();
            await using (var ddl = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await ddl.OpenAsync();
                await using var create = ddl.CreateCommand();
                create.CommandText =
                    "CREATE TABLE koan_hold_parent (id TEXT PRIMARY KEY);" +
                    "CREATE TABLE koan_hold_child (parent TEXT REFERENCES koan_hold_parent (id) DEFERRABLE INITIALLY DEFERRED);";
                await create.ExecuteNonQueryAsync();
            }

            using (await EntityContext.Hold("commit refused"))
            {
                await new Ticket { Title = "never" }.Save();
                (await Data<Ticket, string>.Execute<int>(InstructionSql.NonQuery(
                    "INSERT INTO koan_hold_child (parent) VALUES ('missing')", DataOperationEffect.Write))).Should().Be(1);
                var commit = () => EntityContext.Commit();
                var failure = (await commit.Should().ThrowAsync<TransactionException>()).Which;
                failure.CommitOutcome.Should().Be(DataCommitOutcome.Unknown, "a failed commit cannot say whether the store committed");
                failure.InnerException.Should().BeOfType<SqliteException>().Which.SqliteErrorCode.Should().Be(19);
            }

            (await CommittedCount(path, Tickets)).Should().Be(1, "the transaction rolled back when the scope ended");
            (await CommittedCount(path, "koan_hold_child")).Should().Be(0);
            (await WriteLockIsFree(path)).Should().BeTrue();
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_failed_batch_inside_a_hold_leaves_nothing_of_itself_and_the_hold_goes_on()
    {
        RequireBackingStore();
        var path = TempDatabase("failed-batch");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var before = new Claim { Code = "before" };
            var after = new Claim { Code = "after" };

            using (await EntityContext.Hold("batches"))
            {
                await before.Save();
                var batch = () => Data<Claim, string>.Batch()
                    .Add(new Claim { Code = "twice" })
                    .Add(new Claim { Code = "twice" })
                    .Save();
                await batch.Should().ThrowAsync<UniqueValueConflictException>();
                (await Claim.Query(item => item.Code == "twice")).Should().BeEmpty("an atomic batch that failed left nothing");
                await after.Save();
                await EntityContext.Commit();
            }

            (await Claim.Query(item => item.Code == "twice")).Should().BeEmpty("the hold committed, and nothing of the batch was in it");
            (await Claim.Get(before.Id)).Should().NotBeNull();
            (await Claim.Get(after.Id)).Should().NotBeNull();
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_successful_batch_inside_a_hold_commits_with_it_and_says_it_has_not_yet()
    {
        RequireBackingStore();
        var path = TempDatabase("good-batch");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var ticket = new Ticket { Title = "batched" };

            using (await EntityContext.Hold("batches"))
            {
                var result = await Data<Ticket, string>.Batch().Add(ticket).Save();
                result.Atomicity.Should().Be(BatchAtomicity.Atomic);
                result.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted, "only the scope's commit commits");
                await EntityContext.Commit();
            }

            (await Ticket.Get(ticket.Id)).Should().NotBeNull();
            (await Data<Ticket, string>.Batch().Add(new Ticket { Title = "outside" }).Save())
                .CommitOutcome.Should().Be(DataCommitOutcome.Committed, "outside a hold the batch is its own commit");
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary>More rows than one statement carries, so the conflict falls in a later statement than the rows before it.</summary>
    [Fact]
    public async Task A_failed_bulk_save_across_statements_inside_a_hold_leaves_nothing_of_itself_and_the_hold_goes_on()
    {
        RequireBackingStore();
        var path = TempDatabase("failed-bulk");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var claims = Enumerable.Range(0, 5000).Select(index => new Claim { Code = $"bulk-{index}" }).ToList();
            claims.Add(new Claim { Code = "bulk-0" });
            var after = new Claim { Code = "after" };

            using (await EntityContext.Hold("bulk"))
            {
                var bulk = () => Data<Claim, string>.UpsertMany(claims);
                await bulk.Should().ThrowAsync<UniqueValueConflictException>();
                await after.Save();
                await EntityContext.Commit();
            }

            (await Data<Claim, string>.Count()).Should().Be(1, "only the save after the failure was left to commit");
            (await Claim.Get(after.Id)).Should().NotBeNull();
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_hold_limit_that_is_not_positive_refuses_before_anything_is_held()
    {
        RequireBackingStore();
        var path = TempDatabase("bad-limit");
        try
        {
            await using var host = await BootAsync(Settings(path, holdLimit: TimeSpan.Zero));
            var hold = () => EntityContext.Hold("bad limit");
            hold.Should().Throw<InvalidOperationException>().WithMessage("*HoldLimit must be positive*");
            EntityContext.InTransaction.Should().BeFalse();
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_hold_does_not_nest_with_itself_or_a_deferred_scope()
    {
        RequireBackingStore();
        var path = TempDatabase("nesting");
        try
        {
            await using var host = await BootAsync(Settings(path));
            using (await EntityContext.Hold("outer"))
            {
                var hold = () => EntityContext.Hold("inner");
                hold.Should().Throw<InvalidOperationException>().WithMessage("*nested*");
                var deferred = () => EntityContext.Transaction("inner");
                deferred.Should().Throw<InvalidOperationException>().WithMessage("*nested*");
            }
            using (EntityContext.Transaction("outer"))
            {
                var hold = () => EntityContext.Hold("inner");
                hold.Should().Throw<InvalidOperationException>().WithMessage("*nested*");
                await EntityContext.Rollback();
            }
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_hold_says_what_it_is_and_tracks_nothing()
    {
        RequireBackingStore();
        var path = TempDatabase("self");
        try
        {
            await using var host = await BootAsync(Settings(path));
            Data<Ticket, string>.Capabilities.Has(DataCaps.Write.HeldTransaction).Should().BeTrue();
            using (await EntityContext.Hold("self"))
            {
                var hold = EntityContext.CurrentTransaction!;
                hold.IsHeld.Should().BeTrue();
                hold.Guarantee.Should().Be(TransactionGuarantee.Atomic, "it promises the same at commit");
                hold.Capabilities.Has(TxCaps.Local).Should().BeTrue();
                hold.Capabilities.Has(TxCaps.DeferredCoordination).Should().BeFalse();
                await new Ticket { Title = "acts now" }.Save();
                hold.TrackedOperationCount.Should().Be(0);
                await EntityContext.Rollback();
            }
            using (EntityContext.Transaction("deferred", TransactionGuarantee.Atomic))
                EntityContext.CurrentTransaction!.IsHeld.Should().BeFalse();
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_table_first_used_inside_a_hold_that_rolls_back_is_provisioned_again_next_time()
    {
        RequireBackingStore();
        var path = TempDatabase("provision");
        try
        {
            await using var host = await BootAsync(Settings(path));
            using (await EntityContext.Hold("first use"))
            {
                await new Ticket { Title = "gone" }.Save();
                await EntityContext.Rollback();
            }
            var ticket = await new Ticket { Title = "second use" }.Save();
            (await Ticket.Get(ticket.Id))!.Title.Should().Be("second use");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task One_hold_covers_one_store_so_a_second_route_may_be_read_but_not_written()
    {
        RequireBackingStore();
        var path = TempDatabase("routes");
        var archive = TempDatabase("routes-archive");
        var spelledDifferently = Path.Combine(Path.GetDirectoryName(path)!.ToUpperInvariant(), Path.GetFileName(path));
        try
        {
            await using var host = await BootAsync(new Dictionary<string, string?>(Settings(path), StringComparer.Ordinal)
            {
                ["Koan:Data:Sources:Archive:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Archive:ConnectionString"] = $"Data Source={archive}",
                ["Koan:Data:Sources:Same:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Same:ConnectionString"] = $"Data Source={spelledDifferently}",
            });
            Ticket archived;
            using (EntityContext.Source("Archive")) archived = await new Ticket { Title = "archived" }.Save();
            var ticket = new Ticket { Title = "default" };
            var audit = new Audit { TicketId = "same store" };

            using (await EntityContext.Hold("one store"))
            {
                await ticket.Save();
                using (EntityContext.Source("Same")) await audit.Save();
                using (EntityContext.Source("Archive"))
                {
                    (await Ticket.Get(archived.Id))!.Title.Should().Be("archived", "a read elsewhere sees that store's committed state");
                    var write = () => new Ticket { Title = "elsewhere" }.Save();
                    (await write.Should().ThrowAsync<NotSupportedException>()).Which.Message
                        .Should().Contain("one native transaction covers one route").And.Contain("Archive");
                }
                await EntityContext.Commit();
            }

            (await Ticket.Get(ticket.Id)).Should().NotBeNull();
            using (EntityContext.Source("Same")) (await Audit.Get(audit.Id)).Should().NotBeNull();
        }
        finally
        {
            Delete(path);
            Delete(archive);
        }
    }

    [Fact]
    public async Task A_hold_names_its_route_through_the_ambient_source()
    {
        RequireBackingStore();
        var path = TempDatabase("named-default");
        var archive = TempDatabase("named-archive");
        try
        {
            await using var host = await BootAsync(new Dictionary<string, string?>(Settings(path), StringComparer.Ordinal)
            {
                ["Koan:Data:Sources:Archive:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Archive:ConnectionString"] = $"Data Source={archive}",
            });
            var ticket = new Ticket { Title = "archive" };

            using (EntityContext.Source("Archive"))
            using (await EntityContext.Hold("archive"))
            {
                await ticket.Save();
                (await WriteLockIsFree(archive)).Should().BeFalse("the hold is on the store its source names");
                (await WriteLockIsFree(path)).Should().BeTrue();
                await EntityContext.Commit();
            }

            using (EntityContext.Source("Archive")) (await Ticket.Get(ticket.Id)).Should().NotBeNull();
        }
        finally
        {
            Delete(path);
            Delete(archive);
        }
    }

    [Fact]
    public async Task A_hold_works_on_an_in_memory_database()
    {
        RequireBackingStore();
        await using var host = await BootAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = "Data Source=:memory:",
        });
        var ticket = new Ticket { Title = "memory" };
        using (await EntityContext.Hold("memory"))
        {
            await ticket.Save();
            (await Ticket.Get(ticket.Id)).Should().NotBeNull();
            await EntityContext.Commit();
        }
        (await Ticket.Get(ticket.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Auto_commit_on_dispose_commits_a_hold()
    {
        RequireBackingStore();
        var path = TempDatabase("auto");
        try
        {
            await using var host = await BootAsync(new Dictionary<string, string?>(Settings(path), StringComparer.Ordinal)
            {
                [$"{TransactionOptions.SectionPath}:AutoCommitOnDispose"] = "true",
            });
            var ticket = new Ticket { Title = "auto" };
            using (await EntityContext.Hold("auto")) await ticket.Save();
            (await CommittedCount(path, Tickets)).Should().Be(1);
        }
        finally
        {
            Delete(path);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Two real processes on one file.
    // ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Two_processes_incrementing_one_counter_in_holds_lose_no_update()
    {
        RequireBackingStore();
        var path = TempDatabase("two-processes");
        try
        {
            await using var host = await BootAsync(Settings(path));
            var counter = await new HeldCounter { Value = 0 }.Save();
            const int times = 25;

            await using var first = HeldContender.Process.Start("count", path, counter.Id, times.ToString());
            await using var second = HeldContender.Process.Start("count", path, counter.Id, times.ToString());
            await Task.WhenAll(first.Finished(), second.Finished());
            Output.WriteLine(first.Output);

            (await HeldCounter.Get(counter.Id))!.Value.Should().Be(times * 2, "each read and write ran inside one hold, and the holds took turns across the processes");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_hold_in_this_process_waits_for_the_hold_of_another_and_then_reads_what_it_committed()
    {
        RequireBackingStore();
        var path = TempDatabase("wait-across");
        try
        {
            await using var host = await BootAsync(Settings(path));
            await new HeldNote { Text = "table exists" }.Save();
            var id = Guid.CreateVersion7().ToString("n");

            await using var other = HeldContender.Process.Start("hold", path, id);
            await other.Holding();
            (await HeldNote.Get(id)).Should().BeNull("reads go on during another process's hold, and see committed state");

            var mine = Task.Run(async () =>
            {
                using (await EntityContext.Hold("mine"))
                {
                    var note = await HeldNote.Get(id);
                    note!.Text.Should().Be("written by the other process");
                    await EntityContext.Commit();
                }
            });
            (await Task.WhenAny(mine, Task.Delay(1000))).Should().NotBe(mine, "the other process holds the write lock");

            await other.Say("commit");
            await other.Finished();
            await mine.WaitAsync(TimeSpan.FromSeconds(60));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_waiter_on_another_processs_hold_is_bounded_by_the_busy_timeout()
    {
        RequireBackingStore();
        var path = TempDatabase("bound-across");
        try
        {
            await using var host = await BootAsync(Settings(path, busyTimeout: TimeSpan.FromSeconds(1)));
            await new HeldNote { Text = "table exists" }.Save();

            await using var other = HeldContender.Process.Start("hold", path, Guid.CreateVersion7().ToString("n"));
            await other.Holding();

            var clock = Stopwatch.StartNew();
            var hold = async () => { using (await EntityContext.Hold("mine")) await EntityContext.Commit(); };
            (await hold.Should().ThrowAsync<TransactionException>()).Which.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted);
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));

            clock.Restart();
            var plain = () => new HeldNote { Text = "plain" }.Save();
            (await plain.Should().ThrowAsync<SqliteException>()).Which.SqliteErrorCode.Should().Be(5);
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));

            await other.Say("rollback");
            await other.Finished();
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_process_killed_mid_hold_leaves_nothing_and_frees_the_store()
    {
        RequireBackingStore();
        var path = TempDatabase("killed");
        try
        {
            await using var host = await BootAsync(Settings(path));
            await new HeldNote { Text = "table exists" }.Save();
            var id = Guid.CreateVersion7().ToString("n");

            await using var other = HeldContender.Process.Start("hold", path, id);
            await other.Holding();
            other.Kill();

            using (await EntityContext.Hold("after the kill"))
            {
                (await HeldNote.Get(id)).Should().BeNull("an uncommitted transaction dies with its process");
                await new HeldNote { Text = "goes on" }.Save();
                await EntityContext.Commit();
            }
            (await CommittedCount(path, Notes)).Should().Be(2);
        }
        finally
        {
            Delete(path);
        }
    }
}
