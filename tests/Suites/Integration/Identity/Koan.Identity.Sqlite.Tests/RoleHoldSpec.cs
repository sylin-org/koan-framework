using System.Diagnostics;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Koan.Identity.Roles;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Sqlite.Tests;

/// <summary>
/// SEC-0036: a role, member or overwrite change inside <c>EntityContext.Hold</c> joins that transaction, and outside any scope
/// it is a short hold of its own. A rollback or a failed commit leaves no row, no moved position, no changed bag and no
/// after-event; a commit makes all of it visible at once, and only then. Every handler registered here filters on the keys it
/// owns, because the registry is process-wide.
/// </summary>
public sealed class RoleHoldSpec
{
    public sealed class HeldRoleNote : Entity<HeldRoleNote>
    {
        public string Text { get; set; } = "";
    }

    private static string Temp(string label) => Path.Combine(Path.GetTempPath(), $"koan-role-hold-{label}-{Guid.CreateVersion7():n}.db");

    /// <summary>Work on a flow of its own: it does not inherit the hold, and it reads through the shared caches.</summary>
    private static Task<T> Outside<T>(IServiceProvider services, Func<Task<T>> work)
    {
        using (ExecutionContext.SuppressFlow())
            return Task.Run(async () =>
            {
                using var app = AppHost.PushScope(services);
                return await work();
            });
    }

    private static async Task<IReadOnlyList<string>> Order(RoleCollection roles)
        => (await roles.Page(pageSize: 100)).Items.Select(role => role.Key).ToArray();

    /// <summary>Every role above everyone stands at 1 to their number, each once.</summary>
    private static async Task ShouldBeDense(RoleCollection roles)
    {
        var rows = (await roles.Page(pageSize: 100)).Items.Where(role => role.Key != RoleTokens.Everyone).OrderBy(role => role.Position).ToArray();
        rows.Select(role => role.Position).Should().Equal(Enumerable.Range(1, rows.Length));
    }

    [Fact]
    public async Task A_rollback_leaves_no_row_no_moved_position_and_the_same_bag()
    {
        var path = Temp("rollback");
        try
        {
            await using var host = await RoleSqliteSpec.Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:one", "One", ["global:one"]);
            await roles.Define("role:two", "Two", ["global:two"]);
            await roles.Add("role:one", "person:kept");
            await roles.Overwrite("resource:kept", OverwriteSubjectKind.Role, "role:one", [], ["global:one"]);
            var order = await Order(roles);
            var kept = await roles.Bag("person:kept", true);
            var added = await roles.Bag("person:added", true);

            // Disposed without a commit, and rolled back on purpose: both are the same outcome.
            foreach (var rollBack in new[] { false, true })
            {
                using (await EntityContext.Hold("rolled back"))
                {
                    await roles.Add("role:one", "person:added");
                    await roles.Remove("role:one", "person:kept");
                    await roles.Define("role:new", "New", ["global:new"], position: 1);
                    await roles.SetPermissions("role:two", ["global:changed"]);
                    await roles.Move("role:one", 3);
                    await roles.Overwrite("resource:kept", OverwriteSubjectKind.Person, "person:kept", ["global:one"], []);
                    await roles.RemoveOverwrite("resource:kept", OverwriteSubjectKind.Role, "role:one");
                    await roles.Delete("role:two");
                    if (rollBack) await EntityContext.Rollback();
                }

                (await Order(roles)).Should().Equal(order, "no position moved");
                await ShouldBeDense(roles);
                (await roles.Get("role:new")).Should().BeNull();
                (await roles.Get("role:two"))!.Permissions.Should().Equal("global:two");
                (await roles.Members("role:one")).Items.Select(member => member.PersonId).Should().Equal("person:kept");
                (await roles.Overwrites("resource:kept")).Items.Should().ContainSingle().Which.SubjectId.Should().Be("role:one");
                (await roles.Bag("person:kept", true)).Should().BeSameAs(kept, "no compiled bag was touched");
                (await roles.Bag("person:added", true)).Should().BeSameAs(added, "no compiled bag was touched");
            }

            (await roles.Define("role:after", "After")).Position.Should().Be(3, "the next role takes the place the rollback left");
        }
        finally { RoleSqliteSpec.Delete(path); }
    }

    /// <summary>A command's role change and its entity write: one outcome, whichever way the command answers (DATA-0167).</summary>
    public sealed record GiveRoleAndNote(string Person, string NoteId, Outcome<string> Answer) : ICommand<string>
    {
        public async Task<Outcome<string>> Run(CancellationToken ct)
        {
            await AppHost.GetRequiredService<RoleCollection>("command").Add("role:commanded", Person, ct: ct);
            await new HeldRoleNote { Id = NoteId, Text = "says why" }.Save(ct);
            return Answer;
        }
    }

    [Fact]
    public async Task A_role_change_and_an_entity_write_in_one_command_commit_or_roll_back_together()
    {
        var path = Temp("command");
        try
        {
            await using var host = await RoleSqliteSpec.Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:commanded", "Commanded", ["global:commanded"]);
            await new HeldRoleNote { Text = "table exists" }.Save();
            var bag = await roles.Bag("person:refused", true);

            var refused = await new GiveRoleAndNote("person:refused", "refused", new Outcome<string>.Gated("no")).Send();

            refused.Should().BeOfType<Outcome<string>.Gated>();
            (await roles.Members("role:commanded")).Items.Should().BeEmpty();
            (await HeldRoleNote.Get("refused")).Should().BeNull();
            (await roles.Bag("person:refused", true)).Should().BeSameAs(bag, "no compiled bag was touched");

            var given = await new GiveRoleAndNote("person:given", "given", new Outcome<string>.Done("given")).Send();

            given.Should().BeOfType<Outcome<string>.Done>();
            (await roles.Members("role:commanded")).Items.Select(member => member.PersonId).Should().Equal("person:given");
            (await HeldRoleNote.Get("given")).Should().NotBeNull();
            (await roles.Bag("person:given", true)).Contains("global:commanded").Should().BeTrue();
        }
        finally { RoleSqliteSpec.Delete(path); }
    }

    [Fact]
    public async Task A_commit_is_what_other_flows_see_and_a_bag_read_inside_sees_its_own_change()
    {
        var path = Temp("commit");
        try
        {
            await using var host = await RoleSqliteSpec.Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:base", "Base", ["global:base"]);
            await roles.Add("role:base", "person:held");
            var before = await roles.Bag("person:held", true);
            await new HeldRoleNote { Text = "table exists" }.Save();
            (await roles.OverwritesFor(before, "resource:held")).Should().BeEmpty("this compiles the shared entries a read inside must read past");
            var note = new HeldRoleNote { Text = "with the role" };

            using (await EntityContext.Hold("committed"))
            {
                await note.Save();
                await roles.Define("role:held", "Held", ["global:held"]);
                await roles.Add("role:held", "person:held");
                await roles.SetPermissions("role:base", ["global:changed"]);
                await roles.Overwrite("resource:held", OverwriteSubjectKind.Person, "person:held", [], ["global:held"]);
                await roles.Overwrite("resource:held", OverwriteSubjectKind.Role, "role:base", ["global:held"], []);

                var inside = await roles.Bag("person:held", true);
                inside.InRole("role:held").Should().BeTrue("a read inside the hold sees the hold's change");
                inside.Permissions.Should().BeEquivalentTo(["global:held", "global:changed"]);
                inside.Highest.Should().Be(2);
                (await roles.OverwritesFor(inside, "resource:held")).Select(row => row.SubjectKind).Should()
                    .BeEquivalentTo([OverwriteSubjectKind.Role, OverwriteSubjectKind.Person], "a read inside sees both overwrites it wrote");
                (await roles.Members("role:held")).Items.Should().ContainSingle();

                (await Outside(host.Services, () => roles.Bag("person:held", true))).Should().BeSameAs(before, "nothing uncommitted reached the shared cache");
                (await Outside(host.Services, () => roles.Get("role:held"))).Should().BeNull("the row is not committed");
                (await Outside(host.Services, () => roles.Bag("person:fresh", true))).InRole("role:held").Should().BeFalse("a fresh compile outside reads committed rows");
                (await Outside(host.Services, () => roles.OverwritesFor(before, "resource:held"))).Should().BeEmpty("neither overwrite is committed");
                (await Outside<HeldRoleNote?>(host.Services, () => HeldRoleNote.Get(note.Id))).Should().BeNull();

                await EntityContext.Commit();

                var late = () => roles.Add("role:held", "person:late");
                (await late.Should().ThrowAsync<NotSupportedException>("a hold that has ended has nothing left to join")).Which.Message.Should().Contain("Hold");
            }

            var after = await Outside(host.Services, () => roles.Bag("person:held", true));
            after.Should().NotBeSameAs(before);
            after.InRole("role:held").Should().BeTrue();
            after.Permissions.Should().BeEquivalentTo(["global:held", "global:changed"]);
            (await Outside(host.Services, () => roles.OverwritesFor(after, "resource:held"))).Should().HaveCount(2, "the compiled overwrites followed the commit");
            (await HeldRoleNote.Get(note.Id)).Should().NotBeNull("the role and the row committed as one");
        }
        finally { RoleSqliteSpec.Delete(path); }
    }

    [Fact]
    public async Task A_commit_the_store_refuses_or_that_never_began_leaves_nothing_and_raises_nothing()
    {
        var path = Temp("failed-commit");
        var raised = 0;
        try
        {
            await using var host = await RoleSqliteSpec.Start(path, settings: new()
            {
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path};Pooling=False;Foreign Keys=True",
            });
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await new HeldRoleNote { Text = "table exists" }.Save();
            await roles.Define("role:failed", "Failed", ["global:failed"]);
            await using (var ddl = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await ddl.OpenAsync();
                await using var create = ddl.CreateCommand();
                create.CommandText =
                    "CREATE TABLE role_hold_parent (id TEXT PRIMARY KEY);" +
                    "CREATE TABLE role_hold_child (parent TEXT REFERENCES role_hold_parent (id) DEFERRABLE INITIALLY DEFERRED);";
                await create.ExecuteNonQueryAsync();
            }

            var before = await roles.Bag("person:failed", true);
            Entity.Role.MemberAdded(context =>
            {
                if (context.Current.Key == "role:failed") Interlocked.Increment(ref raised);
                return ValueTask.CompletedTask;
            });

            using (await EntityContext.Hold("refused at commit"))
            {
                await roles.Add("role:failed", "person:failed");
                await Data<HeldRoleNote, string>.Execute<int>(InstructionSql.NonQuery("INSERT INTO role_hold_child (parent) VALUES ('missing')", DataOperationEffect.Write));
                var commit = () => EntityContext.Commit();
                (await commit.Should().ThrowAsync<TransactionException>()).Which.CommitOutcome.Should().Be(DataCommitOutcome.Unknown);
            }

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            using (await EntityContext.Hold("never began"))
            {
                await roles.Add("role:failed", "person:failed");
                var commit = () => EntityContext.Commit(cancelled.Token);
                (await commit.Should().ThrowAsync<TransactionException>()).Which.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted);
            }

            (await roles.Members("role:failed")).Items.Should().BeEmpty();
            (await roles.Bag("person:failed", true)).Should().BeSameAs(before);
            Volatile.Read(ref raised).Should().Be(0);
            await roles.Add("role:failed", "person:failed").WaitAsync(TimeSpan.FromSeconds(30));
            Volatile.Read(ref raised).Should().Be(1, "the store came free, and a change outside a scope has raised by the time it returns");
        }
        finally
        {
            Entity.Role.Reset();
            RoleSqliteSpec.Delete(path);
        }
    }

    [Fact]
    public async Task After_events_wait_for_the_commit_run_outside_the_hold_and_a_failing_one_costs_nothing()
    {
        var path = Temp("after-events");
        var added = new List<string>();
        RoleBag? seen = null;
        RoleBag? seenLater = null;
        ITransactionCoordinator? ambient = null;
        var cancelledToken = false;
        var note = new HeldRoleNote { Text = "from a handler" };
        Exception? refused = null;
        try
        {
            await using var host = await RoleSqliteSpec.Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:events", "Events", ["global:events"]);
            await roles.Define("role:failing", "Failing", ["global:failing"]);
            await roles.Add("role:events", "person:other");
            var otherBefore = await roles.Bag("person:other", true);
            await roles.Bag("person:held", true);
            await roles.Bag("person:later", true);

            Entity.Role.MemberAdded(async context =>
            {
                if (context.Current.Key == "role:failing") throw new InvalidOperationException("a failing handler");
                if (context.Current.Key != "role:events") return;
                lock (added) added.Add(context.Subject!);
                if (context.Subject != "person:held") return;
                ambient = EntityContext.CurrentTransaction;
                cancelledToken = context.CancellationToken.IsCancellationRequested;
                context.Phase.Should().Be(RoleChangePhase.After);
                seen = await roles.Bag(context.Subject!, true);
                seenLater = await roles.Bag("person:later", true);
                await note.Save();
                try { await roles.Add("role:events", "person:recursive"); }
                catch (InvalidOperationException recursive) { refused = recursive; }
            });

            using var change = new CancellationTokenSource();
            using (await EntityContext.Hold("events"))
            {
                await roles.Add("role:failing", "person:held", change.Token);
                await roles.Add("role:events", "person:held", change.Token);
                await roles.SetPermissions("role:events", ["global:changed"], change.Token);
                await roles.Add("role:events", "person:later");
                change.Cancel();
                lock (added) added.Should().BeEmpty("nothing has committed");
                await EntityContext.Commit();
                lock (added) added.Should().Equal("person:held", "person:later");
            }

            cancelledToken.Should().BeFalse("the change's token belongs to the change, and this runs after it");
            ambient.Should().BeNull("an after-event runs outside the hold that has ended");
            seen!.InRole("role:events").Should().BeTrue("its bag was compiled before the hold, and its invalidation ran first");
            seen!.Permissions.Should().Contain("global:changed");
            seenLater!.InRole("role:events").Should().BeTrue("the invalidation of a later change in the hold ran before this after-event too");
            (await HeldRoleNote.Get(note.Id)).Should().NotBeNull("a write from an after-event stands on its own");
            refused.Should().NotBeNull("a role change from a handler is refused as it always was");
            (await roles.Members("role:failing")).Items.Should().ContainSingle("a failing after-event does not undo the commit");
            (await roles.Bag("person:other", true)).Should().NotBeSameAs(otherBefore, "the permissions change reached every bag");

            added.Clear();
            using (await EntityContext.Hold("events rolled back"))
            {
                await roles.Add("role:events", "person:never");
                await EntityContext.Rollback();
            }

            added.Should().BeEmpty("a rollback raises nothing");

            await roles.Add("role:failing", "person:plain");
            (await roles.Members("role:failing")).Items.Should().HaveCount(2, "outside a scope too, a failing after-event is logged, because the commit stands");
        }
        finally
        {
            Entity.Role.Reset();
            RoleSqliteSpec.Delete(path);
        }
    }

    [Fact]
    public async Task A_before_event_joins_the_hold_a_veto_costs_only_its_change_and_a_role_change_from_it_refuses()
    {
        var path = Temp("before-events");
        var note = new HeldRoleNote { Text = "from a before-event" };
        Exception? recursive = null;
        try
        {
            await using var host = await RoleSqliteSpec.Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:before", "Before");
            await roles.Define("role:veto", "Veto");

            Entity.Role.MemberAdding(async context =>
            {
                if (context.Current.Key == "role:veto") return RoleChangeDecision.Veto("no", "not this one");
                if (context.Current.Key != "role:before") return RoleChangeDecision.Continue();
                await note.Save();
                try { await roles.Remove("role:before", "person:nobody"); }
                catch (InvalidOperationException refused) { recursive = refused; }
                return RoleChangeDecision.Continue();
            });

            using (await EntityContext.Hold("rolled back"))
            {
                await roles.Add("role:before", "person:one");
                (await HeldRoleNote.Get(note.Id)).Should().NotBeNull("the handler's write joined the hold");
                await EntityContext.Rollback();
            }

            recursive.Should().NotBeNull();
            (await HeldRoleNote.Get(note.Id)).Should().BeNull("the handler's write rolled back with the hold");

            using (await EntityContext.Hold("vetoed"))
            {
                var vetoed = () => roles.Add("role:veto", "person:one");
                await vetoed.Should().ThrowAsync<UnauthorizedAccessException>();
                await roles.Add("role:before", "person:one");
                await EntityContext.Commit();
            }

            (await roles.Members("role:veto")).Items.Should().BeEmpty();
            (await roles.Members("role:before")).Items.Should().ContainSingle();
            (await HeldRoleNote.Get(note.Id)).Should().NotBeNull();
        }
        finally
        {
            Entity.Role.Reset();
            RoleSqliteSpec.Delete(path);
        }
    }

    [Fact]
    public async Task A_hold_past_its_limit_leaves_nothing_refuses_the_rest_and_frees_other_flows_changes()
    {
        var path = Temp("limit");
        try
        {
            await using var host = await RoleSqliteSpec.Start(path, settings: new() { [$"{TransactionOptions.SectionPath}:HoldLimit"] = TimeSpan.FromSeconds(1).ToString() });
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:limit", "Limit", ["global:limit"]);
            var before = await roles.Bag("person:limit", true);

            using (await EntityContext.Hold("too slow"))
            {
                await roles.Add("role:limit", "person:limit");
                await roles.Define("role:lost", "Lost");
                await Task.Delay(TimeSpan.FromSeconds(2));

                var outside = await Outside(host.Services, () => roles.Define("role:other", "Other")).WaitAsync(TimeSpan.FromSeconds(10));
                outside.Position.Should().Be(2, "the scope is still open, its hold is not, and the position it took came back");

                var change = () => roles.Add("role:limit", "person:more");
                (await change.Should().ThrowAsync<TransactionException>()).Which.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted);
                var read = () => roles.Bag("person:limit", true);
                await read.Should().ThrowAsync<TransactionException>("a bag compiled here would be read outside the transaction");
                var commit = () => EntityContext.Commit();
                (await commit.Should().ThrowAsync<TransactionException>()).Which.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted);
            }

            (await roles.Members("role:limit")).Items.Should().BeEmpty();
            (await roles.Get("role:lost")).Should().BeNull();
            (await roles.Bag("person:limit", true)).InRole("role:limit").Should().BeFalse();
            await ShouldBeDense(roles);
        }
        finally { RoleSqliteSpec.Delete(path); }
    }

    /// <summary>
    /// A backlog is the normal state of an application that runs its commands one at a time. A plain change takes its place in
    /// the same queue, so it lands behind whoever is ahead, and every command behind it lands too.
    /// </summary>
    [Fact]
    public async Task A_plain_change_beside_a_backlog_of_commands_lands_and_so_does_every_command()
    {
        var path = Temp("backlog");
        try
        {
            await using var host = await RoleSqliteSpec.Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:plain", "Plain");
            await roles.Define("role:held", "Held");
            await new HeldRoleNote { Text = "table exists" }.Save();

            using var stop = new CancellationTokenSource();
            var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var queue = Outside(host.Services, async () =>
            {
                var committed = 0;
                while (!stop.IsCancellationRequested)
                {
                    using (await EntityContext.Hold("command"))
                    {
                        await new HeldRoleNote { Text = $"command {committed}" }.Save();
                        running.TrySetResult();
                        await Task.Delay(20);
                        await roles.Add("role:held", $"person:{committed}");
                        await EntityContext.Commit();
                    }

                    committed++;
                }

                return committed;
            });
            await running.Task.WaitAsync(TimeSpan.FromSeconds(30));

            var plain = Outside(host.Services, () => roles.Add("role:plain", "person:plain"));
            await plain.WaitAsync(TimeSpan.FromSeconds(10));
            stop.Cancel();
            var commands = await queue.WaitAsync(TimeSpan.FromSeconds(30));

            (await roles.Members("role:plain")).Items.Should().ContainSingle();
            var members = 0;
            for (string? after = null; ;)
            {
                var page = await roles.Members("role:held", after, take: 100);
                members += page.Items.Count;
                if ((after = page.Next) is null) break;
            }

            members.Should().Be(commands, "every command landed");
        }
        finally { RoleSqliteSpec.Delete(path); }
    }

    [Fact]
    public async Task A_plain_change_is_a_hold_of_its_own_and_one_under_way_keeps_a_hold_behind_it()
    {
        var path = Temp("own-hold");
        var underWay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var goOn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ITransactionCoordinator? inside = null;
        try
        {
            await using var host = await RoleSqliteSpec.Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:plain", "Plain");
            await roles.Define("role:held", "Held");
            Entity.Role.MemberAdding(async context =>
            {
                if (context.Subject != "person:plain") return RoleChangeDecision.Continue();
                inside = EntityContext.CurrentTransaction;
                underWay.TrySetResult();
                await goOn.Task;
                return RoleChangeDecision.Continue();
            });

            var plain = Outside(host.Services, () => roles.Add("role:plain", "person:plain"));
            await underWay.Task.WaitAsync(TimeSpan.FromSeconds(30));
            inside.Should().NotBeNull();
            inside!.IsHeld.Should().BeTrue("on a route that offers holds, a change outside a scope is one");

            var behind = Outside(host.Services, () => roles.Add("role:held", "person:held"));
            var hold = Outside(host.Services, async () =>
            {
                using (await EntityContext.Hold("behind it"))
                {
                    await roles.Add("role:held", "person:hold");
                    await EntityContext.Commit();
                }

                return true;
            });
            (await Task.WhenAny(behind, hold, Task.Delay(300))).Should().NotBeOfType<Task<RoleMember>>("the change under way has the store");
            hold.IsCompleted.Should().BeFalse();

            goOn.TrySetResult();
            await Task.WhenAll(plain, behind, hold).WaitAsync(TimeSpan.FromSeconds(30));
            (await roles.Members("role:plain")).Items.Should().ContainSingle();
            (await roles.Members("role:held")).Items.Should().HaveCount(2);
        }
        finally
        {
            goOn.TrySetResult();
            Entity.Role.Reset();
            RoleSqliteSpec.Delete(path);
        }
    }

    /// <summary>A deletion is many writes. Outside a scope too it is one outcome, so one that stops part way leaves the role whole.</summary>
    [Fact]
    public async Task A_plain_deletion_that_stops_part_way_leaves_the_role_as_it_was()
    {
        var path = Temp("stopped");
        using var stop = new CancellationTokenSource();
        try
        {
            await using var host = await KoanIntegrationHost.Configure().WithSettings(new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path};Pooling=False",
            }).ConfigureServices(services => services.AddKoan(() => RoleMember.Lifecycle.BeforeRemove(context =>
            {
                if (context.Current.PersonId == "person:stops:here") stop.Cancel();
                return context.Proceed();
            }))).StartAsync();
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:stops", "Stops", ["global:stops"]);
            await roles.Add("role:stops", "person:stops:here");
            await roles.Add("role:stops", "person:stops:next");
            await roles.Overwrite("resource:stops", OverwriteSubjectKind.Role, "role:stops", ["global:stops"], []);

            var delete = () => roles.Delete("role:stops", stop.Token);
            await delete.Should().ThrowAsync<OperationCanceledException>();

            (await roles.Get("role:stops"))!.Permissions.Should().Equal("global:stops");
            (await roles.Members("role:stops")).Items.Should().HaveCount(2);
            (await roles.Overwrites("resource:stops")).Items.Should().ContainSingle().Which.Allow.Should().Equal("global:stops");
            (await roles.Delete("role:stops")).Should().BeTrue("nothing was left behind to stop a later one");
        }
        finally { RoleSqliteSpec.Delete(path); }
    }

    /// <summary>A change waiting in the queue observes its token, and leaves the queue and the store as it found them.</summary>
    [Fact]
    public async Task A_plain_change_cancelled_in_the_queue_ends_at_once_and_leaves_nothing()
    {
        var path = Temp("cancelled");
        try
        {
            await using var host = await RoleSqliteSpec.Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:queue", "Queue");
            using var cancel = new CancellationTokenSource();

            using (await EntityContext.Hold("ahead"))
            {
                await roles.Add("role:queue", "person:ahead");
                var waiting = Outside(host.Services, () => roles.Add("role:queue", "person:cancelled", cancel.Token));
                (await Task.WhenAny(waiting, Task.Delay(200))).Should().NotBe(waiting);
                cancel.Cancel();
                var ended = await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(5)));
                ended.Should().Be(waiting, "it ends while the hold ahead of it is still open");
                var outcome = () => waiting;
                await outcome.Should().ThrowAsync<OperationCanceledException>();
                await EntityContext.Commit();
            }

            await roles.Add("role:queue", "person:after").WaitAsync(TimeSpan.FromSeconds(10));
            (await roles.Members("role:queue")).Items.Select(member => member.PersonId).Should().Equal("person:after", "person:ahead");
        }
        finally { RoleSqliteSpec.Delete(path); }
    }

    /// <summary>
    /// A first use inside a hold would meet a read on its way to the same table: the read provisions on a connection of its own,
    /// waiting for the hold's store, while the hold waits for that provisioning. So the tables exist before anything can.
    /// </summary>
    [Fact]
    public async Task On_a_fresh_store_the_tables_exist_at_boot_so_a_first_change_and_a_first_bag_cannot_meet_on_them()
    {
        var path = Temp("fresh");
        try
        {
            await using var host = await RoleSqliteSpec.Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                foreach (var type in new[] { typeof(Role), typeof(RoleMember), typeof(PermissionOverwrite), typeof(AuditEvent) })
                    (await RoleSqliteSpec.Scalar(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{type.FullName}'"))
                        .Should().Be(1L, "{0} exists before anything used it", type.Name);
            }

            await new HeldRoleNote { Text = "table exists" }.Save();

            // A read first, then the hold's change.
            using (await EntityContext.Hold("first change"))
            {
                await new HeldRoleNote { Text = "the hold has the store" }.Save();
                var read = Outside(host.Services, () => roles.Bag("person:fresh", true));
                await Task.Delay(300);
                await roles.Define("role:fresh", "Fresh", ["global:fresh"]).WaitAsync(TimeSpan.FromSeconds(10));
                await roles.Add("role:fresh", "person:fresh").WaitAsync(TimeSpan.FromSeconds(10));
                (await read.WaitAsync(TimeSpan.FromSeconds(10))).InRole("role:fresh").Should().BeFalse("it reads what is committed");
                await EntityContext.Commit();
            }

            (await roles.Bag("person:fresh", true)).InRole("role:fresh").Should().BeTrue();

            // The hold's change first, then a read.
            using (await EntityContext.Hold("second change"))
            {
                await roles.Overwrite("resource:fresh", OverwriteSubjectKind.Role, "role:fresh", [], ["global:fresh"]);
                var bag = await Outside(host.Services, () => roles.Bag("person:other", true)).WaitAsync(TimeSpan.FromSeconds(10));
                (await Outside(host.Services, () => roles.OverwritesFor(bag, "resource:fresh")).WaitAsync(TimeSpan.FromSeconds(10))).Should().BeEmpty();
                await EntityContext.Commit();
            }
        }
        finally { RoleSqliteSpec.Delete(path); }
    }

    /// <summary>
    /// Why another flow's change waits for the whole hold: deciding beside it would place a role from the committed top, and
    /// write it over the place the hold had taken.
    /// </summary>
    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 2)]
    public async Task Another_flows_change_decides_only_after_the_hold_has_ended(bool commit, int position)
    {
        var path = Temp("waits");
        try
        {
            await using var host = await RoleSqliteSpec.Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:base", "Base");
            Task<Role> other;

            using (await EntityContext.Hold("first"))
            {
                await roles.Define("role:held", "Held");
                other = Outside(host.Services, () => roles.Define("role:other", "Other"));
                (await Task.WhenAny(other, Task.Delay(300))).Should().NotBe(other, "it decides from what it reads, and the hold has yet to end");
                (await roles.Bag("person:reader", true)).Should().NotBeNull("a read does not wait");
                (await Outside(host.Services, () => roles.Bag("person:reader", true)).WaitAsync(TimeSpan.FromSeconds(10))).Should().NotBeNull("nor does a read elsewhere");
                if (commit) await EntityContext.Commit();
                else await EntityContext.Rollback();
            }

            (await other.WaitAsync(TimeSpan.FromSeconds(60))).Position.Should().Be(position);
            await ShouldBeDense(roles);
        }
        finally { RoleSqliteSpec.Delete(path); }
    }

    [Fact]
    public async Task The_flows_of_one_hold_take_turns_and_holds_and_plain_changes_together_keep_the_order_dense()
    {
        var path = Temp("many");
        try
        {
            await using var host = await RoleSqliteSpec.Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:shared", "Shared");

            using (await EntityContext.Hold("flows"))
            {
                await Task.WhenAll(Enumerable.Range(0, 20).Select(i => i % 4 == 0
                    ? (Task)roles.Define($"role:flow:{i}", $"Flow {i}")
                    : roles.Add("role:shared", $"person:flow:{i}")));
                await EntityContext.Commit();
            }

            (await roles.Members("role:shared")).Items.Should().HaveCount(15);
            await ShouldBeDense(roles);

            await Task.WhenAll(Enumerable.Range(0, 24).Select(i => Outside(host.Services, async () =>
            {
                if (i % 2 == 0) return await roles.Add(await Plain(i), $"person:{i}");
                using (await EntityContext.Hold($"hold {i}"))
                {
                    await roles.Define($"role:held:{i}", $"Held {i}");
                    var member = await roles.Add($"role:held:{i}", $"person:{i}");
                    await EntityContext.Commit();
                    return member;
                }

                async Task<string> Plain(int at) => (await roles.Define($"role:plain:{at}", $"Plain {at}")).Key;
            }))).WaitAsync(TimeSpan.FromSeconds(60));

            await ShouldBeDense(roles);
            (await roles.Page(pageSize: 100)).TotalCount.Should().Be(1 + 5 + 24);
            foreach (var i in Enumerable.Range(0, 24))
                (await roles.Bag($"person:{i}", true)).Roles.Should().HaveCount(3);
        }
        finally { RoleSqliteSpec.Delete(path); }
    }
}
