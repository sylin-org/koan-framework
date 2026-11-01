using System.Diagnostics;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Koan.Identity.Roles;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Sqlite.Tests;

/// <summary>
/// SEC-0036: a role, member or overwrite change inside <c>EntityContext.Hold</c> joins that transaction. A rollback or a
/// failed commit leaves no row, no moved position, no changed bag and no after-event; a commit makes all of it visible at
/// once, and only then. Every handler registered here filters on the keys it owns, because the registry is process-wide.
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
            await roles.Overwrites("resource:held"); // first use of a table belongs outside a hold (DATA-0166)
            await new HeldRoleNote { Text = "table exists" }.Save();
            var note = new HeldRoleNote { Text = "with the role" };

            using (await EntityContext.Hold("committed"))
            {
                await note.Save();
                await roles.Define("role:held", "Held", ["global:held"]);
                await roles.Add("role:held", "person:held");
                await roles.SetPermissions("role:base", ["global:changed"]);
                await roles.Overwrite("resource:held", OverwriteSubjectKind.Person, "person:held", [], ["global:held"]);

                var inside = await roles.Bag("person:held", true);
                inside.InRole("role:held").Should().BeTrue("a read inside the hold sees the hold's change");
                inside.Permissions.Should().BeEquivalentTo(["global:held", "global:changed"]);
                inside.Highest.Should().Be(2);
                (await roles.OverwritesFor(inside, "resource:held")).Should().ContainSingle();
                (await roles.Members("role:held")).Items.Should().ContainSingle();

                (await Outside(host.Services, () => roles.Bag("person:held", true))).Should().BeSameAs(before, "nothing uncommitted reached the shared cache");
                (await Outside(host.Services, () => roles.Get("role:held"))).Should().BeNull("the row is not committed");
                (await Outside(host.Services, () => roles.Bag("person:fresh", true))).InRole("role:held").Should().BeFalse("a fresh compile outside reads committed rows");
                var outsideRows = await Outside(host.Services, async () =>
                {
                    var bag = new RoleBag("person:held", true, [], []);
                    return await roles.OverwritesFor(bag, "resource:held");
                });
                outsideRows.Should().BeEmpty("the overwrite is not committed");
                (await Outside<HeldRoleNote?>(host.Services, () => HeldRoleNote.Get(note.Id))).Should().BeNull();

                await EntityContext.Commit();

                var late = () => roles.Add("role:held", "person:late");
                (await late.Should().ThrowAsync<NotSupportedException>("a hold that has ended has nothing left to join")).Which.Message.Should().Contain("Hold");
            }

            var after = await Outside(host.Services, () => roles.Bag("person:held", true));
            after.Should().NotBeSameAs(before);
            after.InRole("role:held").Should().BeTrue();
            after.Permissions.Should().BeEquivalentTo(["global:held", "global:changed"]);
            (await Outside(host.Services, () => roles.OverwritesFor(after, "resource:held"))).Should().ContainSingle("the compiled overwrite followed the commit");
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
            Volatile.Read(ref raised).Should().Be(1, "the gate came free, and a change outside a scope raises at once");
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

            Entity.Role.MemberAdded(async context =>
            {
                if (context.Current.Key == "role:failing") throw new InvalidOperationException("a failing handler");
                if (context.Current.Key != "role:events") return;
                lock (added) added.Add(context.Subject!);
                ambient = EntityContext.CurrentTransaction;
                cancelledToken = context.CancellationToken.IsCancellationRequested;
                context.Phase.Should().Be(RoleChangePhase.After);
                seen = await roles.Bag(context.Subject!, true);
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
                change.Cancel();
                lock (added) added.Should().BeEmpty("nothing has committed");
                await EntityContext.Commit();
                lock (added) added.Should().Equal("person:held");
            }

            cancelledToken.Should().BeFalse("the change's token belongs to the change, and this runs after it");
            ambient.Should().BeNull("an after-event runs outside the hold that has ended");
            seen!.InRole("role:events").Should().BeTrue("every invalidation of the hold ran before its first after-event");
            seen!.Permissions.Should().Contain("global:changed");
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
    /// The inversion SEC-0036 answers: a change already under way needs the store the hold keeps, and the hold's first change
    /// needs it to finish. The hold's change refuses within its short wait, not at the hold's limit, and then both can go on.
    /// </summary>
    [Fact]
    public async Task A_change_under_way_that_needs_the_store_costs_the_holds_change_its_short_wait_and_not_a_stall()
    {
        var path = Temp("inversion");
        var underWay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var goOn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var host = await RoleSqliteSpec.Start(path, settings: new() { [$"{RoleOptions.SectionPath}:HeldChangeWait"] = TimeSpan.FromMilliseconds(500).ToString() });
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:plain", "Plain");
            await roles.Define("role:held", "Held");
            Entity.Role.MemberAdding(async context =>
            {
                if (context.Subject != "person:plain") return RoleChangeDecision.Continue();
                underWay.TrySetResult();
                await goOn.Task;
                return RoleChangeDecision.Continue();
            });

            var plain = Outside(host.Services, () => roles.Add("role:plain", "person:plain"));
            await underWay.Task.WaitAsync(TimeSpan.FromSeconds(30));

            using (await EntityContext.Hold("second to the gate"))
            {
                goOn.TrySetResult();
                var began = Stopwatch.GetTimestamp();
                var change = () => roles.Add("role:held", "person:held");
                (await change.Should().ThrowAsync<TimeoutException>()).Which.Message.Should().Contain(nameof(RoleOptions.HeldChangeWait));
                Stopwatch.GetElapsedTime(began).Should().BeLessThan(TimeSpan.FromSeconds(10), "the hold's limit is 20 seconds and the store's busy timeout 30");
                plain.IsCompleted.Should().BeFalse("it is waiting for the store this hold keeps");
                await EntityContext.Rollback();
            }

            await plain.WaitAsync(TimeSpan.FromSeconds(60));
            (await roles.Members("role:plain")).Items.Should().ContainSingle("the change under way landed once the store was free");

            using (await EntityContext.Hold("after it"))
            {
                await roles.Add("role:held", "person:held").WaitAsync(TimeSpan.FromSeconds(10));
                await EntityContext.Commit();
            }

            (await roles.Members("role:held")).Items.Should().ContainSingle("the refusal left the gate as it found it");
        }
        finally
        {
            goOn.TrySetResult();
            Entity.Role.Reset();
            RoleSqliteSpec.Delete(path);
        }
    }

    [Fact]
    public async Task A_change_under_way_that_finishes_without_the_store_only_delays_the_hold()
    {
        var path = Temp("delay");
        var underWay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var host = await RoleSqliteSpec.Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:plain", "Plain");
            await roles.Define("role:held", "Held");
            Entity.Role.MemberAdding(async context =>
            {
                if (context.Subject != "person:vetoed") return RoleChangeDecision.Continue();
                underWay.TrySetResult();
                await Task.Delay(300);
                return RoleChangeDecision.Veto("no", "not this one");
            });

            var vetoed = Outside(host.Services, async () =>
            {
                try { await roles.Add("role:plain", "person:vetoed"); return false; }
                catch (UnauthorizedAccessException) { return true; }
            });
            await underWay.Task.WaitAsync(TimeSpan.FromSeconds(30));

            using (await EntityContext.Hold("behind it"))
            {
                await roles.Add("role:held", "person:held");
                await EntityContext.Commit();
            }

            (await vetoed.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();
            (await roles.Members("role:held")).Items.Should().ContainSingle();
        }
        finally
        {
            Entity.Role.Reset();
            RoleSqliteSpec.Delete(path);
        }
    }

    /// <summary>
    /// Why the gate outlasts the change: a lock released when the change returned would let another flow place a role from
    /// the committed top, and write it over the place the hold had taken.
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
            await roles.Bag("person:first", true); // first use of a table belongs outside a hold (DATA-0166)
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
    public async Task The_flows_of_one_hold_take_turns_many_holds_never_refuse_each_other_and_a_plain_change_beside_a_hold_keeps_the_order_dense()
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

            await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Outside(host.Services, () => InAHold(roles, $"role:parallel:{i}", $"person:{i}"))))
                .WaitAsync(TimeSpan.FromSeconds(60));
            await ShouldBeDense(roles);

            for (var round = 0; round < 10; round++)
            {
                var key = $"role:round:{round}";
                var plain = Outside(host.Services, () => roles.Define($"{key}:plain", "Plain"));
                var held = Outside(host.Services, async () =>
                {
                    for (var tries = 1; ; tries++)
                    {
                        // A refused hold has freed the store for the change it met: give that change the time to take it.
                        try { return await InAHold(roles, $"{key}:held", "person:round"); }
                        catch (TimeoutException) when (tries < 10) { await Task.Delay(300); }
                    }
                });
                await Task.WhenAll(plain, held).WaitAsync(TimeSpan.FromSeconds(60));
            }

            await ShouldBeDense(roles);
            (await roles.Page(pageSize: 100)).TotalCount.Should().Be(1 + 5 + 8 + 20);
            (await roles.Bag("person:round", true)).Roles.Should().HaveCount(10 + 2);
        }
        finally { RoleSqliteSpec.Delete(path); }

        static async Task<RoleMember> InAHold(RoleCollection roles, string role, string person)
        {
            using (await EntityContext.Hold(role))
            {
                await roles.Define(role, role);
                var member = await roles.Add(role, person);
                await EntityContext.Commit();
                return member;
            }
        }
    }
}
