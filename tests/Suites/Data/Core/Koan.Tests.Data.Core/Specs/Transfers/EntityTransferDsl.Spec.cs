using System.Collections.Concurrent;
using System.Collections.Frozen;
using Koan.Core.Capabilities;
using Koan.Data.Core.Model;
using Koan.Data.Core.Pipeline;
using Koan.Data.Core.Transfers;
using Koan.Data.Core.Querying;
using Koan.Tests.Data.Core.Support;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Transfers;

public sealed class EntityTransferDslSpec
{
    private static readonly string[] Partitions =
    [
        "active",
        "inactive",
        "hot",
        "archive",
        "batch",
        "dest",
        "mirror",
        "filtered",
        "sync",
        "sync-target",
        "reporting",
        "secondary"
    ];

    private readonly ITestOutputHelper _output;

    public EntityTransferDslSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Copy_ToPartition_CopiesFilteredEntities()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("active"))
        {
            await new TransferTodo { Title = "A", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            await new TransferTodo { Title = "B", Active = false, UpdatedAt = DateTime.UtcNow }.Save();
        }

        var audits = new List<TransferAuditBatch>();
        var result = await TransferTodo.Copy(p => p.Active)
            .From(partition: "active")
            .To(partition: "inactive")
            .Audit(audits.Add)
            .Run();

        result.Kind.Should().Be(TransferKind.Copy);
        result.CopiedCount.Should().Be(1);
        result.DeletedCount.Should().Be(0);
        result.ReadCount.Should().Be(1);
        result.Warnings.Should().BeEmpty();
        audits.Should().NotBeEmpty();
        audits.Last().IsSummary.Should().BeTrue();

        using (runtime.UsePartition("inactive"))
        {
            var items = await TransferTodo.All();
            items.Should().ContainSingle(x => x.Title == "A");
        }

        using (runtime.UsePartition("active"))
        {
            var items = await TransferTodo.All();
            items.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Copy_Predicate_AppliesFilter()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        await new TransferTodo { Title = "keep", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
        await new TransferTodo { Title = "drop", Active = true, UpdatedAt = DateTime.UtcNow }.Save();

        var result = await TransferTodo.Copy(todo => todo.Title == "keep")
            .To(partition: "filtered")
            .Run();

        result.CopiedCount.Should().Be(1);

        using (runtime.UsePartition("filtered"))
        {
            var items = await TransferTodo.All();
            items.Should().ContainSingle(t => t.Title == "keep");
        }
    }

    [Fact]
    public async Task Move_DefaultStrategy_RemovesFromSource()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("hot"))
        {
            for (var i = 0; i < 3; i++)
            {
                await new TransferTodo { Title = $"Item {i}", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            }
        }

        var result = await TransferTodo.Move(p => true)
            .From(partition: "hot")
            .To(partition: "archive")
            .Run();

        result.Kind.Should().Be(TransferKind.Move);
        result.CopiedCount.Should().Be(3);
        result.DeletedCount.Should().Be(3);

        using (runtime.UsePartition("hot"))
        {
            (await TransferTodo.All()).Should().BeEmpty();
        }

        using (runtime.UsePartition("archive"))
        {
            (await TransferTodo.All()).Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task Move_BatchedStrategy_RespectsBatching()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("batch"))
        {
            for (var i = 0; i < 4; i++)
            {
                await new TransferTodo { Title = $"Batch {i}", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            }
        }

        var result = await TransferTodo.Move()
            .From(partition: "batch")
            .To(partition: "dest")
            .Batch(1)
            .Run();

        result.CopiedCount.Should().Be(4);
        result.DeletedCount.Should().Be(4);

        using (runtime.UsePartition("batch"))
        {
            (await TransferTodo.All()).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Move_SyncedStrategy_RemovesAsItGoes()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("sync"))
        {
            for (var i = 0; i < 2; i++)
            {
                await new TransferTodo { Title = $"Sync {i}", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            }
        }

        var result = await TransferTodo.Move()
            .From(partition: "sync")
            .To(partition: "sync-target")
            .Batch(1)
            .Run();

        result.CopiedCount.Should().Be(2);
        result.DeletedCount.Should().Be(2);

        using (runtime.UsePartition("sync"))
        {
            (await TransferTodo.All()).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Mirror_Push_SynchronizesToTarget()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        await new TransferTodo { Title = "primary", Active = true, UpdatedAt = DateTime.UtcNow }.Save();

        var result = await TransferTodo.Mirror()
            .To(partition: "mirror")
            .Run();

        result.Kind.Should().Be(TransferKind.Mirror);
        result.CopiedCount.Should().Be(1);
        result.DeletedCount.Should().Be(0);

        using (runtime.UsePartition("mirror"))
        {
            (await TransferTodo.All()).Should().ContainSingle(x => x.Title == "primary");
        }
    }

    [Fact]
    public async Task Mirror_Pull_SynchronizesBackToDefault()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("mirror"))
        {
            await new TransferTodo { Title = "remote", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
        }

        var result = await TransferTodo.Mirror(mode: MirrorMode.Pull)
            .To(partition: "mirror")
            .Run();

        result.CopiedCount.Should().Be(1);

        var all = await TransferTodo.All();
        all.Should().ContainSingle(x => x.Title == "remote");
    }

    /// <summary>
    /// NK-252. A Pull writes into From(), and its batch audits used to report From and To swapped while the summary
    /// reported them as declared; neither said which store or container was used. Every event now carries the declared
    /// From and To, a direction, and the places both sides resolved to, and the result says how the source was read.
    /// </summary>
    [Theory]
    [InlineData(MirrorMode.Push, TransferDirection.IntoDestination)]
    [InlineData(MirrorMode.Pull, TransferDirection.IntoSource)]
    public async Task Mirror_ReportsDeclaredSidesADirectionAndTheResolvedPlaces(
        MirrorMode mode,
        TransferDirection written)
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var authoritative = mode == MirrorMode.Push ? "active" : "mirror";
        using (runtime.UsePartition(authoritative))
            await new TransferTodo { Title = "only", Active = true }.Save();

        var audits = new List<TransferAuditBatch>();
        var result = await TransferTodo.Mirror(mode)
            .From(partition: "active")
            .To(partition: "mirror")
            .Audit(audits.Add)
            .Run();

        result.CopiedCount.Should().Be(1);
        result.ResolvedFrom.Should().Be(new TransferEndpoint("Default", TransferAdapterFactory.ProviderId, "TransferTodo#active"));
        result.ResolvedTo.Should().Be(new TransferEndpoint("Default", TransferAdapterFactory.ProviderId, "TransferTodo#mirror"));
        result.ReadStrategy.Should().Be(TransferReadStrategy.Streamed);
        audits.Should().OnlyContain(audit => audit.From.Partition == "active" && audit.To.Partition == "mirror");
        audits.Should().OnlyContain(audit => audit.ResolvedFrom == result.ResolvedFrom && audit.ResolvedTo == result.ResolvedTo);
        audits.Where(audit => !audit.IsSummary).Should().ContainSingle()
            .Which.Direction.Should().Be(written);
    }

    [Fact]
    public async Task Mirror_Bidirectional_UsesTimestampForResolution()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        var primary = await new TransferTodo { Title = "v1", Active = true, UpdatedAt = DateTime.UtcNow.AddMinutes(-2) }.Save();

        using (runtime.UsePartition("reporting"))
        {
            await new TransferTodo { Id = primary.Id, Title = "v2", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
        }

        var result = await TransferTodo.Mirror(mode: MirrorMode.Bidirectional)
            .To(partition: "reporting")
            .Run();

        result.Conflicts.Should().BeEmpty();

        var updated = await TransferTodo.Get(primary.Id);
        updated.Should().NotBeNull();
        updated!.Title.Should().Be("v2");

        using (runtime.UsePartition("reporting"))
        {
            var target = await TransferTodo.Get(primary.Id);
            target.Should().NotBeNull();
            target!.Title.Should().Be("v2");
        }
    }

    [Fact]
    public async Task Mirror_Bidirectional_WithoutTimestamp_ReportsConflicts()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        var baseNote = await new BasicNote { Content = "default" }.Save();

        using (runtime.UsePartition("secondary"))
        {
            await new BasicNote { Id = baseNote.Id, Content = "secondary" }.Save();
        }

        var result = await BasicNote.Mirror(mode: MirrorMode.Bidirectional)
            .To(partition: "secondary")
            .Run();

        result.Conflicts.Should().NotBeEmpty();
        result.CopiedCount.Should().Be(0);
        result.Warnings.Should().Contain(w => w.Contains("No [Timestamp]"));

        var defaultNote = await BasicNote.Get(baseNote.Id);
        defaultNote.Should().NotBeNull();
        defaultNote!.Content.Should().Be("default");
    }

    /// <summary>
    /// NK-108. Past Batch() unresolved overlaps the bidirectional mirror threw, after the target had already received
    /// the source-only rows of earlier batches and before the target-only rows reached the source. It now finishes the
    /// run, lists the first Batch() conflicts and says how many it left out.
    /// </summary>
    [Fact]
    public async Task Mirror_Bidirectional_PastTheConflictBound_FinishesAndReportsTheFirstConflicts()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        foreach (var id in new[] { "o1", "o2", "o3" })
        {
            await new BasicNote { Id = id, Content = "default" }.Save();
            using (runtime.UsePartition("secondary")) await new BasicNote { Id = id, Content = "secondary" }.Save();
        }
        await new BasicNote { Id = "s1", Content = "source-only" }.Save();
        using (runtime.UsePartition("secondary")) await new BasicNote { Id = "t1", Content = "target-only" }.Save();

        var result = await BasicNote.Mirror(MirrorMode.Bidirectional)
            .To(partition: "secondary")
            .Batch(2)
            .Run();

        result.Conflicts.Select(conflict => conflict.Id).Should().Equal("o1", "o2");
        result.Warnings.Should().Contain(warning => warning.Contains("3 overlapping identities were left unresolved"));
        result.CopiedCount.Should().Be(2, "the source-only and target-only rows both cross");
        (await BasicNote.Get("t1")).Should().NotBeNull();
        using (runtime.UsePartition("secondary")) (await BasicNote.Get("s1")).Should().NotBeNull();
        (await BasicNote.Get("o3"))!.Content.Should().Be("default", "an unresolved overlap changes neither side");
    }

    /// <summary>
    /// NK-001. When the target wins, the bidirectional mirror writes the target's row into the source. It used to
    /// journal the whole entity through lossy JSON (defaults omitted, dictionary keys recased, no non-public setters,
    /// seeded collections appended), so the source received a degraded copy. The winner must arrive intact.
    /// </summary>
    [Theory]
    [InlineData(MirrorConflict.Destination)]
    [InlineData(MirrorConflict.Latest)]
    public async Task Mirror_Bidirectional_TargetWinsWritesTheTargetRowIntact(MirrorConflict policy)
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var source = new MirrorFlag { Enabled = true, Tags = ["source"], UpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        source.AssignOwner("source-owner");
        source = await source.Save();

        using (runtime.UsePartition("secondary"))
        {
            var target = new MirrorFlag
            {
                Id = source.Id,
                Enabled = false,
                Meta = new Dictionary<string, string> { ["SeoTitle"] = "target" },
                Tags = ["target"],
                UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            target.AssignOwner("target-owner");
            await target.Save();
        }

        var result = await MirrorFlag.Mirror(MirrorMode.Bidirectional)
            .To(partition: "secondary")
            .Conflict(policy)
            .Run();

        result.Conflicts.Should().BeEmpty();
        var mirrored = await MirrorFlag.Get(source.Id);
        mirrored.Should().NotBeNull();
        using var scope = new AwesomeAssertions.Execution.AssertionScope();
        mirrored!.Enabled.Should().BeFalse("a stored default must not come back as the initializer");
        mirrored.Owner.Should().Be("target-owner", "a non-public setter must be restored");
        mirrored.Meta.Should().ContainKey("SeoTitle", "dictionary keys are data and keep their case");
        mirrored.Tags.Should().Equal(["target"], "a stored collection replaces the seeded one");
    }

    /// <summary>
    /// NK-011 / NK-013. Without a source partition, MoveToPartition deleted from a partition literally named "root",
    /// so a move out of the base set left the row in place and became a silent copy.
    /// </summary>
    [Fact]
    public async Task MoveToPartition_FromTheBaseSet_RemovesTheBaseRow()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var todo = await new TransferTodo { Title = "base", Active = true }.Save();

        await todo.MoveToPartition<TransferTodo, string>("archive");

        (await TransferTodo.Get(todo.Id)).Should().BeNull("the row left the base set");
        using (runtime.UsePartition("archive"))
            (await TransferTodo.Get(todo.Id)).Should().NotBeNull();
    }

    /// <summary>NK-011 / NK-013. A move whose source is its target upserted the row and then deleted it.</summary>
    [Fact]
    public async Task MoveToPartition_IntoItsOwnAmbientPartition_IsRefusedAndKeepsTheRow()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        using (runtime.UsePartition("archive"))
        {
            var todo = await new TransferTodo { Title = "archived", Active = true }.Save();

            var refusal = await Record.ExceptionAsync(() => todo.MoveToPartition<TransferTodo, string>("archive"));

            (await TransferTodo.Get(todo.Id)).Should().NotBeNull("a move onto its own container must not delete it");
            refusal.Should().BeOfType<ArgumentException>();
        }
    }

    [Fact]
    public async Task MoveToPartition_FromAnExplicitPartition_RemovesItThere()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        TransferTodo todo;
        using (runtime.UsePartition("hot"))
            todo = await new TransferTodo { Title = "hot", Active = true }.Save();

        await todo.MoveToPartition<TransferTodo, string>("archive", fromPartition: "hot");

        using (runtime.UsePartition("hot"))
            (await TransferTodo.Get(todo.Id)).Should().BeNull();
        using (runtime.UsePartition("archive"))
            (await TransferTodo.Get(todo.Id)).Should().NotBeNull();
    }

    /// <summary>
    /// Already-compiled callers bind to <c>Task MoveToPartition</c>; a changed return type is a binary break. The
    /// outcome-reporting form is <c>TryMoveToPartition</c>.
    /// </summary>
    [Fact]
    public void MoveToPartition_KeepsItsPlainTaskReturnType_AndTryReportsTheOutcome()
    {
        var methods = typeof(EntityPartitionMoveExtensions).GetMethods();

        var moves = methods.Where(m => m.Name == "MoveToPartition").ToArray();
        moves.Should().HaveCount(2, "the keyed and the string-keyed overload");
        moves.Should().OnlyContain(m => m.ReturnType == typeof(Task));

        var tries = methods.Where(m => m.Name == "TryMoveToPartition").ToArray();
        tries.Should().HaveCount(2, "the same overloads as MoveToPartition");
        tries.Should().OnlyContain(m => m.ReturnType == typeof(Task<bool>));
    }

    [Fact]
    public async Task TryMoveToPartition_InfersTheStringKeyAndReportsAMissingSourceRow()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var saved = await new TransferTodo { Title = "saved", Active = true }.Save();
        var unsaved = new TransferTodo { Title = "never saved", Active = true };

        (await saved.TryMoveToPartition("archive")).Should().BeTrue();
        (await unsaved.TryMoveToPartition("archive")).Should().BeFalse("the base set held no row to remove");
        (await saved.TryMoveToPartition("dest", fromPartition: "archive", copy: true)).Should().BeTrue();

        using (runtime.UsePartition("archive"))
            (await TransferTodo.All()).Should().HaveCount(2);
        using (runtime.UsePartition("dest"))
            (await TransferTodo.Get(saved.Id)).Should().NotBeNull();
    }

    /// <summary>
    /// NK-018. Latest took the first [Timestamp] property, whatever its kind. With a set-once CreatedAt declared
    /// before the OnSave UpdatedAt (Koan's own Identity shape), copies compared equal and edits never reconciled.
    /// </summary>
    [Fact]
    public async Task Mirror_Latest_ComparesTheOnSaveTimestampNotTheCreationStamp()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var created = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var source = await new AuditedNote { Content = "original", CreatedAt = created }.Save();
        await Task.Delay(20);
        using (runtime.UsePartition("secondary"))
            await new AuditedNote { Id = source.Id, Content = "edited later", CreatedAt = created }.Save();

        var result = await AuditedNote.Mirror(MirrorMode.Bidirectional).To(partition: "secondary").Run();

        result.Conflicts.Should().BeEmpty();
        result.CopiedCount.Should().Be(1, "the later edit wins and is written back");
        (await AuditedNote.Get(source.Id))!.Content.Should().Be("edited later");
    }

    [Fact]
    public async Task Mirror_Latest_WithTwoOnSaveTimestamps_ReportsTheOverlapInsteadOfGuessing()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var source = await new TwiceStampedNote { Content = "source" }.Save();
        using (runtime.UsePartition("secondary"))
            await new TwiceStampedNote { Id = source.Id, Content = "target" }.Save();

        var result = await TwiceStampedNote.Mirror(MirrorMode.Bidirectional).To(partition: "secondary").Run();

        result.Conflicts.Should().ContainSingle();
        result.Warnings.Should().Contain(warning => warning.Contains("ambiguous", StringComparison.Ordinal));
        (await TwiceStampedNote.Get(source.Id))!.Content.Should().Be("source");
    }

    /// <summary>
    /// NK-019. A filtered Push mirror tested replica rows against the authoritative side without the predicate, so a
    /// row that left the predicate there stayed in the replica with its last matching copy.
    /// </summary>
    [Theory]
    [InlineData(MirrorMode.Push)]
    [InlineData(MirrorMode.Pull)]
    public async Task Mirror_WithPredicate_RemovesReplicaRowsWhoseAuthoritativeRowLeftThePredicate(MirrorMode mode)
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        // Push: the base set is authoritative and "mirror" the replica. Pull swaps them.
        var authoritative = mode == MirrorMode.Push ? "" : "mirror";
        var replica = mode == MirrorMode.Push ? "mirror" : "";
        TransferTodo todo;
        using (EntityContext.Partition(authoritative))
            todo = await new TransferTodo { Title = "published", Active = true }.Save();

        (await TransferTodo.Mirror(todo => todo.Active, mode).To(partition: "mirror").Run()).CopiedCount.Should().Be(1);

        using (EntityContext.Partition(authoritative))
        {
            todo.Active = false;
            await todo.Save();
        }

        var second = await TransferTodo.Mirror(todo => todo.Active, mode).To(partition: "mirror").Run();

        second.DeletedCount.Should().Be(1);
        using (EntityContext.Partition(replica))
            (await TransferTodo.Get(todo.Id)).Should().BeNull("the authoritative row no longer matches the mirror");
        using (EntityContext.Partition(authoritative))
            (await TransferTodo.Get(todo.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task To_WithSourceAndAdapter_ShouldThrow()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        Action act = () => TransferTodo.Copy().To(source: "primary", adapter: "sqlite");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Batch_BoundsProviderPagesAndDestinationWrites()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        using (runtime.UsePartition("active"))
            for (var i = 0; i < 5; i++)
                await new TransferTodo { Title = $"bounded-{i}", Active = true }.Save();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();
        var audits = new List<TransferAuditBatch>();

        var result = await TransferTodo.Copy()
            .From(partition: "active")
            .To(partition: "archive")
            .Batch(2)
            .Audit(audits.Add)
            .Run();

        result.CopiedCount.Should().Be(5);
        repository.MaxRequestedPageSize.Should().Be(2);
        repository.MaxUpsertManyCount.Should().Be(2);
        audits.Where(batch => !batch.IsSummary).Should().OnlyContain(batch => batch.BatchCount <= 2);
        audits.Last().IsSummary.Should().BeTrue();
    }

    /// <summary>
    /// NK-306. Another writer removes a row the Move already copied, behind its read. A numbered source page shifts
    /// down by one, so the next page used to start one row late: the skipped row was never copied and stayed in the
    /// source while the Move reported success. The read continues after the last identity it returned instead.
    /// </summary>
    [Fact]
    public async Task Move_ARowDeletedBehindTheRead_DoesNotMakeTheMoveSkipTheNextRow()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        using (runtime.UsePartition("hot"))
            foreach (var id in new[] { "a1", "a2", "a3" })
                await new TransferTodo { Id = id, Title = id, Active = true }.Save();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();

        var result = await TransferTodo.Move()
            .From(partition: "hot")
            .To(partition: "archive")
            .Batch(1)
            .Audit(batch =>
            {
                if (batch is { IsSummary: false, BatchNumber: 1 })
                    repository.BeforeNextQuery = () => repository.Delete("a1").GetAwaiter().GetResult();
            })
            .Run();

        result.CopiedCount.Should().Be(3, "every row present for the whole read is copied");
        result.DeletedCount.Should().Be(2, "the row another writer removed is not counted as moved");
        using (runtime.UsePartition("hot"))
            (await TransferTodo.All()).Should().BeEmpty();
        using (runtime.UsePartition("archive"))
            (await TransferTodo.All()).Select(todo => todo.Id).Should().BeEquivalentTo(["a1", "a2", "a3"]);
    }

    /// <summary>
    /// NK-205. A row with a lower identity enters the Move's predicate between two source pages. A numbered page
    /// counted it and read a row it had already copied a second time. The row that entered behind the read is not
    /// read at all, so it is neither copied nor deleted, and nothing is read twice.
    /// </summary>
    [Fact]
    public async Task Move_ARowEnteringThePredicateBehindTheRead_IsNotCopiedAndNothingIsReadTwice()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        using (runtime.UsePartition("hot"))
        {
            await new TransferTodo { Id = "a", Title = "a", Active = false }.Save();
            foreach (var id in new[] { "b", "c", "d", "e" })
                await new TransferTodo { Id = id, Title = id, Active = true }.Save();
        }

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();

        var result = await TransferTodo.Move(todo => todo.Active)
            .From(partition: "hot")
            .To(partition: "archive")
            .Batch(2)
            .Audit(batch =>
            {
                if (batch is { IsSummary: false, BatchNumber: 1 })
                    repository.BeforeNextQuery = () => repository
                        .Upsert(new TransferTodo { Id = "a", Title = "a", Active = true }).GetAwaiter().GetResult();
            })
            .Run();

        result.ReadCount.Should().Be(4);
        result.CopiedCount.Should().Be(4);
        result.DeletedCount.Should().Be(4);
        result.Warnings.Should().BeEmpty();
        using (runtime.UsePartition("hot"))
            (await TransferTodo.All()).Select(todo => todo.Id).Should().Equal("a");
        using (runtime.UsePartition("archive"))
            (await TransferTodo.All()).Select(todo => todo.Id).Should().BeEquivalentTo(["b", "c", "d", "e"]);
    }

    /// <summary>NK-254. A transfer counts past Int32: its totals and the audit's running numbers are 64-bit.</summary>
    [Fact]
    public void Transfer_counters_are_64_bit()
    {
        typeof(TransferResult<string>).GetProperties()
            .Where(property => property.Name.EndsWith("Count", StringComparison.Ordinal))
            .Should().OnlyContain(property => property.PropertyType == typeof(long));
        typeof(TransferAuditBatch).GetProperty(nameof(TransferAuditBatch.BatchNumber))!.PropertyType.Should().Be(typeof(long));
        typeof(TransferAuditBatch).GetProperty(nameof(TransferAuditBatch.TotalProcessed))!.PropertyType.Should().Be(typeof(long));
    }

    [Fact]
    public async Task Copy_AmbiguousDestinationFault_IsNotReplayed()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        using (runtime.UsePartition("active"))
            await new TransferTodo { Title = "one-dispatch", Active = true }.Save();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();
        repository.ThrowAfterNextUpsertMany = true;

        var act = () => TransferTodo.Copy()
            .From(partition: "active")
            .To(partition: "archive")
            .Batch(1)
            .Run();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("injected ambiguous destination fault");
        repository.UpsertManyCalls.Should().Be(1);
    }

    /// <summary>
    /// DATA-0113. This spec previously asserted that a transfer over an adapter without provider-bounded
    /// paging throws <c>QueryStreamRejectedException</c>. That contradicted the real-adapter conformance
    /// kit — which asserts transfers work and had been red for InMemory, JSON, and Redis ever since — and
    /// it left <c>Copy</c>/<c>Move</c> broken on JSON, the Data pillar's own floor adapter. The transfer
    /// DSL now reads with the strongest strategy the provider supports, and says which one it used.
    /// </summary>
    [Fact]
    public async Task Copy_MissingBoundedPaging_MaterializesTheSourceAndReportsIt()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        using (runtime.UsePartition("active"))
            for (var i = 0; i < 3; i++)
                await new TransferTodo { Title = $"unbounded-{i}", Active = true }.Save();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();
        repository.AdvertiseBoundedPaging = false;

        var result = await TransferTodo.Copy()
            .From(partition: "active")
            .To(partition: "archive")
            .Batch(2)
            .Run();

        result.CopiedCount.Should().Be(3, "an unqualified provider must not silently drop the transfer");
        result.Warnings.Should().Contain(
            warning => warning.Contains("provider-bounded paging"),
            "materializing instead of streaming is reported, never silent");

        // Writes stay batched even when the read could not be: 3 rows at Batch(2) is two dispatches.
        repository.UpsertManyCalls.Should().Be(2);
    }

    /// <summary>
    /// NK-251. A builder kept its warnings on itself, so a second Run reported what the first one saw: the materialized
    /// read notice came back on a run that streamed. Each Run reports its own warnings.
    /// </summary>
    [Fact]
    public async Task Run_TwiceOnOneBuilder_ReportsEachRunsOwnWarnings()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        using (runtime.UsePartition("active"))
            await new TransferTodo { Title = "reused", Active = true }.Save();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();
        repository.AdvertiseBoundedPaging = false;
        var builder = TransferTodo.Copy().From(partition: "active").To(partition: "archive");

        (await builder.Run()).Warnings.Should().ContainSingle(warning => warning.Contains("provider-bounded paging"));

        repository.AdvertiseBoundedPaging = true;
        (await builder.Run()).Warnings.Should().BeEmpty("the second run streamed and saw nothing to warn about");
    }

    [Fact]
    public async Task Copy_CancellationBeforeCandidatePage_PreventsDestinationDispatch()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();
        using var cancellation = new CancellationTokenSource();
        repository.BeforeNextQuery = cancellation.Cancel;

        var act = () => TransferTodo.Copy()
            .To(partition: "archive")
            .Batch(2)
            .Run(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        repository.QueryCalls.Should().Be(1);
        repository.UpsertManyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Copy_InvalidPageReceipt_FailsBeforeDestinationDispatch()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();
        repository.HandlePagination = false;

        var act = () => TransferTodo.Copy()
            .To(partition: "archive")
            .Batch(2)
            .Run();

        await act.Should().ThrowAsync<QueryStreamRejectedException>();
        repository.QueryCalls.Should().Be(1);
        repository.UpsertManyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Copy_SameContext_IsNoOpBeforeProviderWork()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();

        var result = await TransferTodo.Copy()
            .From(partition: "active")
            .To(partition: "active")
            .Run();

        result.CopiedCount.Should().Be(0);
        result.Warnings.Should().ContainSingle(warning => warning.Contains("was skipped", StringComparison.Ordinal));
        repository.QueryCalls.Should().Be(0);
        repository.UpsertManyCalls.Should().Be(0);
    }

    /// <summary>
    /// NK-006 / NK-048 (DATA-0125). An unset To() axis inherits the ambient partition, so this Move is written as
    /// two endpoints and resolves to one container. It used to copy the rows onto themselves and delete them.
    /// </summary>
    [Fact]
    public async Task Move_DestinationInheritingTheAmbientPartition_IsRefusedBeforeProviderWork()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        using (runtime.UsePartition("hot"))
        {
            for (var i = 0; i < 3; i++)
                await new TransferTodo { Title = $"ambient-{i}", Active = true }.Save();
            repository.ResetObservations();

            var refusal = await Record.ExceptionAsync(() => TransferTodo.Move().From(partition: "hot").To().Run());
            var (queries, upserts) = (repository.QueryCalls, repository.UpsertManyCalls);

            (await TransferTodo.All()).Should().HaveCount(3, "a Move onto its own container must not delete anything");
            refusal.Should().BeOfType<InvalidOperationException>()
                .Which.Message.Should().Contain("would delete every row it copied").And.Contain("TransferTodo#hot");
            queries.Should().Be(0);
            upserts.Should().Be(0);
        }
    }

    [Fact]
    public async Task MoveAndMirror_SameDeclaredContext_AreRefused()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        using (runtime.UsePartition("active"))
            await new TransferTodo { Title = "kept", Active = true }.Save();

        await FluentActions.Invoking(() => TransferTodo.Move().From(partition: "active").To(partition: "active").Run())
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Invoking(() => TransferTodo.Mirror(MirrorMode.Bidirectional)
                .From(partition: "active").To(partition: "active").Run())
            .Should().ThrowAsync<InvalidOperationException>();

        using (runtime.UsePartition("active"))
            (await TransferTodo.All()).Should().ContainSingle(todo => todo.Title == "kept");
    }

    [Fact]
    public async Task Mirror_Push_RemovesReplicaOnlyIdentitiesAfterBoundedRead()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        TransferTodo current;
        using (runtime.UsePartition("active"))
            current = await new TransferTodo { Title = "current", Active = true }.Save();
        using (runtime.UsePartition("archive"))
        {
            await new TransferTodo { Id = current.Id, Title = "old", Active = true }.Save();
            await new TransferTodo { Title = "replica-only", Active = true }.Save();
        }

        var result = await TransferTodo.Mirror()
            .From(partition: "active")
            .To(partition: "archive")
            .Batch(1)
            .Run();

        result.CopiedCount.Should().Be(1);
        result.DeletedCount.Should().Be(1);
        using (runtime.UsePartition("archive"))
            (await TransferTodo.All()).Should().ContainSingle(todo => todo.Id == current.Id && todo.Title == "current");
    }

    /// <summary>
    /// NK-050 (DATA-0149). A transfer used to write every row as a Save, so a Copy marked every row fresh and an archiving
    /// Move replaced every last-modified value with the move time. By default it now carries the stored values.
    /// </summary>
    [Theory]
    [InlineData(TransferKind.Copy)]
    [InlineData(TransferKind.Move)]
    [InlineData(TransferKind.Mirror)]
    public async Task Transfer_KeepsStoredTimestampsByDefault(TransferKind kind)
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var created = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        AuditedNote stored;
        using (runtime.UsePartition("hot"))
            stored = await new AuditedNote { Content = "kept", CreatedAt = created }.Save();
        await Task.Delay(30);

        var result = kind switch
        {
            TransferKind.Copy => await AuditedNote.Copy().From(partition: "hot").To(partition: "archive").Run(),
            TransferKind.Move => await AuditedNote.Move().From(partition: "hot").To(partition: "archive").Run(),
            _ => await AuditedNote.Mirror().From(partition: "hot").To(partition: "archive").Run()
        };

        result.CopiedCount.Should().Be(1);
        using (runtime.UsePartition("archive"))
        {
            var archived = (await AuditedNote.Get(stored.Id))!;
            archived.UpdatedAt.Should().Be(stored.UpdatedAt, "an exact transfer keeps the OnSave timestamp it read");
            archived.CreatedAt.Should().Be(created);
        }
    }

    /// <summary>
    /// NK-050 (DATA-0149). Load, Upsert and Remove lifecycle and registered write stamps no longer run for a transfer's
    /// own reads and writes, so an AfterLoad that mutates its entity is not persisted to the destination, a validator
    /// cannot cancel a Move partway, and a Move fires no Remove hook per moved row.
    /// </summary>
    [Theory]
    [InlineData(TransferKind.Copy)]
    [InlineData(TransferKind.Move)]
    [InlineData(TransferKind.Mirror)]
    public async Task Transfer_RunsNoLifecycleOrWriteStampsByDefault(TransferKind kind)
    {
        await using var runtime = await CreateRuntime(HookedNote.Register);
        await Reset(runtime);
        runtime.BindHost();

        HookedNote seeded;
        using (runtime.UsePartition("hot"))
            seeded = await new HookedNote { Content = "stored" }.Save();
        HookedNote.Counts.Clear();

        var result = kind switch
        {
            TransferKind.Copy => await HookedNote.Copy().From(partition: "hot").To(partition: "archive").Run(),
            TransferKind.Move => await HookedNote.Move().From(partition: "hot").To(partition: "archive").Run(),
            _ => await HookedNote.Mirror().From(partition: "hot").To(partition: "archive").Run()
        };

        result.CopiedCount.Should().Be(1);
        result.DeletedCount.Should().Be(kind == TransferKind.Move ? 1 : 0);
        HookedNote.Counts.Should().BeEmpty("no Lifecycle or write stamp runs for an exact transfer");
        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>().Repository<HookedNote>();
        using (runtime.UsePartition("archive"))
            (await repository.Get(seeded.Id))!.Content.Should().Be("stored", "the AfterLoad mutation is not persisted");
    }

    /// <summary>NK-050 (DATA-0149). Save semantics remain available as an explicit choice.</summary>
    [Fact]
    public async Task Transfer_WithSemanticFidelity_WritesEachRowAsASave()
    {
        await using var runtime = await CreateRuntime(HookedNote.Register);
        await Reset(runtime);
        runtime.BindHost();

        HookedNote seeded;
        using (runtime.UsePartition("hot"))
            seeded = await new HookedNote { Content = "stored" }.Save();
        HookedNote.Counts.Clear();
        await Task.Delay(30);

        var result = await HookedNote.Move()
            .From(partition: "hot")
            .To(partition: "archive")
            .Fidelity(TransferFidelity.Semantic)
            .Run();

        result.CopiedCount.Should().Be(1);
        HookedNote.Counts.Should().ContainKeys("load", "upsert", "remove", "stamp");
        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>().Repository<HookedNote>();
        using (runtime.UsePartition("archive"))
        {
            var archived = (await repository.Get(seeded.Id))!;
            archived.Content.Should().Be("stored|loaded", "a Semantic transfer persists what Load lifecycle produced");
            archived.UpdatedAt.Should().BeAfter(seeded.UpdatedAt, "a Semantic write stamps OnSave with the transfer time");
        }
    }

    /// <summary>
    /// The audit callback is application code: work it does runs as ordinary Entity work, not as part of the transfer.
    /// </summary>
    [Fact]
    public async Task Transfer_AuditCallbackRunsAsOrdinaryEntityWork()
    {
        await using var runtime = await CreateRuntime(HookedNote.Register);
        await Reset(runtime);
        runtime.BindHost();

        using (runtime.UsePartition("hot"))
            await new HookedNote { Content = "stored" }.Save();
        HookedNote.Counts.Clear();

        await HookedNote.Copy()
            .From(partition: "hot")
            .To(partition: "archive")
            .Audit(batch =>
            {
                if (!batch.IsSummary) new HookedNote { Content = "audit" }.Save().GetAwaiter().GetResult();
            })
            .Run();

        HookedNote.Counts.Should().ContainKeys("upsert", "stamp");
    }

    /// <summary>
    /// NK-109. A bidirectional mirror restamped the winner's OnSave timestamp on the losing side, so every run found the
    /// other side newer, rewrote it, and Latest compared mirror times instead of edit times. An exact mirror writes the
    /// winner's own timestamp: the second run finds both sides equal and writes nothing, and a later edit still wins.
    /// </summary>
    [Fact]
    public async Task Mirror_Bidirectional_ConvergesAndKeepsTheWinnersTimestamp()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var source = await new AuditedNote { Content = "v2" }.Save();
        await Task.Delay(30);
        AuditedNote target;
        using (runtime.UsePartition("secondary"))
            target = await new AuditedNote { Id = source.Id, Content = "v2" }.Save();

        Task<TransferResult<string>> Mirror()
            => AuditedNote.Mirror(MirrorMode.Bidirectional).To(partition: "secondary").Run();

        (await Mirror()).CopiedCount.Should().Be(1, "the newer target row is written back to the source");
        (await AuditedNote.Get(source.Id))!.UpdatedAt.Should().Be(target.UpdatedAt);
        (await Mirror()).CopiedCount.Should().Be(0, "both sides now hold the same edit time");
        (await Mirror()).CopiedCount.Should().Be(0);

        await Task.Delay(30);
        var edited = await new AuditedNote { Id = source.Id, Content = "v3" }.Save();
        (await Mirror()).CopiedCount.Should().Be(1, "an edit made after the mirror is newer than the mirrored copy");
        using (runtime.UsePartition("secondary"))
        {
            var mirrored = (await AuditedNote.Get(source.Id))!;
            mirrored.Content.Should().Be("v3");
            mirrored.UpdatedAt.Should().Be(edited.UpdatedAt);
        }
        (await Mirror()).CopiedCount.Should().Be(0);
    }

    /// <summary>
    /// NK-250. A transfer learned that a side it writes is read-only only at its first write there: a Move from a
    /// read-only source copied the whole selection and then failed at the first delete. Every side a transfer writes is
    /// checked before the first read.
    /// </summary>
    [Fact]
    public async Task Transfer_ThatWritesAReadOnlySource_IsRefusedBeforeItReadsOrWrites()
    {
        await using var runtime = await CreateRuntime(settings: ReadOnlyLegacy);
        await Reset(runtime);
        runtime.BindHost();

        var factory = runtime.Services.GetRequiredService<TransferAdapterFactory>();
        var legacy = factory.Repository<TransferTodo>("Legacy");
        await legacy.Upsert(new TransferTodo { Id = "legacy-1", Title = "legacy", Active = true });
        await new TransferTodo { Id = "local-1", Title = "local", Active = true }.Save();
        legacy.ResetObservations();
        var local = factory.Repository<TransferTodo>();
        local.ResetObservations();

        var move = await Record.ExceptionAsync(() => TransferTodo.Move().From(source: "Legacy").To(partition: "archive").Run());
        move.Should().BeOfType<DataSourcePolicyException>();
        var copy = await Record.ExceptionAsync(() => TransferTodo.Copy().To(source: "Legacy", partition: "archive").Run());
        copy.Should().BeOfType<DataSourcePolicyException>();

        legacy.QueryCalls.Should().Be(0, "the Move is refused before it reads its read-only origin");
        local.QueryCalls.Should().Be(0, "the Copy is refused before it reads its origin");
        local.UpsertManyCalls.Should().Be(0);
        using (runtime.UsePartition("archive"))
            (await TransferTodo.All()).Should().BeEmpty();
    }

    /// <summary>
    /// NK-253. Admission counted the side a transfer only reads as a mutation, so a Copy out of the default failed while a
    /// cutover had closed the default's writes, and held the drain open while it ran. The side read is admitted as a read.
    /// </summary>
    [Fact]
    public async Task Copy_FromARouteWhoseWritesAreClosed_RunsAndMoveIsRefused()
    {
        await using var runtime = await CreateRuntime(settings: Archive);
        await Reset(runtime);
        runtime.BindHost();

        await new TransferTodo { Id = "kept", Title = "kept", Active = true }.Save();
        var route = ((RepositoryFacade<TransferTodo, string>)runtime.Services.GetRequiredService<IDataService>()
            .GetRepository<TransferTodo, string>()).RouteBinding!;
        var horizon = runtime.Services.GetRequiredService<DataOperationHorizon>();

        await using (await horizon.CloseAndDrain(
                         [new DataRouteMaintenanceRequest(route, BlockReads: false, BlockWrites: true)],
                         CancellationToken.None))
        {
            var copied = await TransferTodo.Copy().To(source: "Archive", partition: "archive").Run();
            copied.CopiedCount.Should().Be(1, "a Copy only reads the default");

            var move = await Record.ExceptionAsync(() => TransferTodo.Move().To(source: "Archive", partition: "cold").Run());
            move.Should().BeOfType<DataSwitchInProgressException>("a Move deletes from the default");
        }

        (await TransferTodo.Get("kept")).Should().NotBeNull();
    }

    private static readonly IReadOnlyDictionary<string, string?> ReadOnlyLegacy = new Dictionary<string, string?>
    {
        ["Koan:Data:Sources:Legacy:Adapter"] = TransferAdapterFactory.ProviderId,
        ["Koan:Data:Sources:Legacy:ConnectionString"] = "legacy",
        ["Koan:Data:Sources:Legacy:Access"] = "ReadOnly"
    };

    private static readonly IReadOnlyDictionary<string, string?> Archive = new Dictionary<string, string?>
    {
        ["Koan:Data:Sources:Archive:Adapter"] = TransferAdapterFactory.ProviderId,
        ["Koan:Data:Sources:Archive:ConnectionString"] = "archive"
    };

    private static Task<DataCoreRuntimeFixture> CreateRuntime() => CreateRuntime(null);

    private static Task<DataCoreRuntimeFixture> CreateRuntime(
        Action? compose = null,
        IReadOnlyDictionary<string, string?>? settings = null)
        => DataCoreRuntimeFixture.CreateAsync(extraSettings: settings, configureServices: services =>
        {
            compose?.Invoke();
            services.AddSingleton<TransferAdapterFactory>();
            services.AddSingleton<IDataAdapterFactory>(provider =>
                provider.GetRequiredService<TransferAdapterFactory>());
        });

    private static async ValueTask Reset(DataCoreRuntimeFixture runtime)
    {
        runtime.ResetEntityCaches();

        await TransferTodo.RemoveAll();
        await BasicNote.RemoveAll();
        await MirrorFlag.RemoveAll();
        await AuditedNote.RemoveAll();
        await TwiceStampedNote.RemoveAll();
        await HookedNote.RemoveAll();

        foreach (var partition in Partitions)
        {
            using var lease = runtime.UsePartition(partition);
            await TransferTodo.RemoveAll();
            await BasicNote.RemoveAll();
            await MirrorFlag.RemoveAll();
            await AuditedNote.RemoveAll();
            await TwiceStampedNote.RemoveAll();
        await HookedNote.RemoveAll();
        }
    }

    [DataAdapter(TransferAdapterFactory.ProviderId)]
    private sealed class TransferTodo : Entity<TransferTodo>
    {
        public string Title { get; set; } = "";
        public bool Active { get; set; }

        [Timestamp(OnSave = true)]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    [DataAdapter(TransferAdapterFactory.ProviderId)]
    private sealed class BasicNote : Entity<BasicNote>
    {
        public string Content { get; set; } = "";
    }

    [DataAdapter(TransferAdapterFactory.ProviderId)]
    private sealed class MirrorFlag : Entity<MirrorFlag>
    {
        public bool Enabled { get; set; } = true;
        public string Owner { get; private set; } = "unassigned";
        public Dictionary<string, string> Meta { get; set; } = new();
        public List<string> Tags { get; set; } = ["seeded"];

        // Set once and given explicitly, so Latest deterministically picks the target.
        [Timestamp]
        public DateTime UpdatedAt { get; set; }

        public void AssignOwner(string owner) => Owner = owner;
    }

    [DataAdapter(TransferAdapterFactory.ProviderId)]
    private sealed class AuditedNote : Entity<AuditedNote>
    {
        public string Content { get; set; } = "";

        // Declared first on purpose: a set-once creation stamp that copies share.
        [Timestamp]
        public DateTimeOffset CreatedAt { get; set; }

        [Timestamp(OnSave = true)]
        public DateTimeOffset UpdatedAt { get; set; }
    }

    [DataAdapter(TransferAdapterFactory.ProviderId)]
    private sealed class TwiceStampedNote : Entity<TwiceStampedNote>
    {
        public string Content { get; set; } = "";

        [Timestamp(OnSave = true)]
        public DateTimeOffset ModifiedAt { get; set; }

        [Timestamp(OnSave = true)]
        public DateTimeOffset TouchedAt { get; set; }
    }

    /// <summary>Counts every Lifecycle hook and write stamp that runs for it, and marks what Load produced.</summary>
    [DataAdapter(TransferAdapterFactory.ProviderId)]
    private sealed class HookedNote : Entity<HookedNote>
    {
        public static ConcurrentDictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

        public string Content { get; set; } = "";

        [Timestamp(OnSave = true)]
        public DateTimeOffset UpdatedAt { get; set; }

        public static void Register()
        {
            Lifecycle.AfterLoad(context =>
            {
                Tally("load");
                context.Current.Content += "|loaded";
            });
            Lifecycle.BeforeUpsert(context => { Tally("upsert"); return context.Proceed(); });
            Lifecycle.BeforeRemove(context => { Tally("remove"); return context.Proceed(); });
            StorageWriteContributorRegistry.Register(new WriteStampContributor(
                "transfer-spec-counting-stamp",
                type => type == typeof(HookedNote) ? new CountingStamp() : null));
        }

        private static void Tally(string hook) => Counts.AddOrUpdate(hook, 1, static (_, count) => count + 1);

        private sealed class CountingStamp : IWriteStamp
        {
            public int Priority => 200;
            public void Apply(object entity) => Tally("stamp");
        }
    }

    private sealed class TransferAdapterFactory : IDataAdapterFactory
    {
        public const string ProviderId = "transfer-spec";
        private readonly ConcurrentDictionary<(Type Entity, string Source), object> _repositories = new();

        public string Provider => ProviderId;

        public TransferRepository<TEntity> Repository<TEntity>(string source = "Default")
            where TEntity : class, IEntity<string>
            => (TransferRepository<TEntity>)_repositories.GetOrAdd(
                (typeof(TEntity), source),
                static key => Activator.CreateInstance(typeof(TransferRepository<>).MakeGenericType(key.Entity))!);

        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
            IServiceProvider services,
            string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
        {
            if (typeof(TKey) != typeof(string) ||
                (typeof(TEntity) != typeof(TransferTodo) && typeof(TEntity) != typeof(BasicNote) &&
                 typeof(TEntity) != typeof(MirrorFlag) && typeof(TEntity) != typeof(AuditedNote) &&
                 typeof(TEntity) != typeof(TwiceStampedNote) && typeof(TEntity) != typeof(HookedNote)))
                throw new InvalidOperationException($"The transfer spec adapter cannot create {typeof(TEntity).Name}<{typeof(TKey).Name}>.");

            var repository = _repositories.GetOrAdd(
                (typeof(TEntity), source),
                static key => Activator.CreateInstance(typeof(TransferRepository<>).MakeGenericType(key.Entity))!);
            return (IDataRepository<TEntity, TKey>)repository;
        }

        // Each source is its own set of in-memory partitions, so two source names are two stores.
        public ValueTask<bool> ProveDistinctStores(
            IServiceProvider services,
            string source,
            string other,
            CancellationToken ct = default)
            => ValueTask.FromResult(!string.Equals(source, other, StringComparison.OrdinalIgnoreCase));

        public StorageNamingCapability GetNamingCapability(IServiceProvider services)
            => new()
            {
                Style = StorageNamingStyle.EntityType,
                Casing = NameCasing.AsIs,
                PartitionSeparator = '#',
                Partition = PartitionTokenPolicy.Default
            };
    }

    private sealed class TransferRepository<TEntity> :
        IDataRepository<TEntity, string>,
        IQueryRepository<TEntity, string>,
        IDescribesCapabilities
        where TEntity : class, IEntity<string>
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TEntity>> _partitions =
            new(StringComparer.Ordinal);

        public bool AdvertiseBoundedPaging { get; set; } = true;
        public bool HandlePagination { get; set; } = true;
        public bool ThrowAfterNextUpsertMany { get; set; }
        public Action? BeforeNextQuery { get; set; }
        public int QueryCalls { get; private set; }
        public int UpsertManyCalls { get; private set; }
        public int MaxRequestedPageSize { get; private set; }
        public int MaxUpsertManyCount { get; private set; }

        public void ResetObservations()
        {
            AdvertiseBoundedPaging = true;
            HandlePagination = true;
            ThrowAfterNextUpsertMany = false;
            BeforeNextQuery = null;
            QueryCalls = 0;
            UpsertManyCalls = 0;
            MaxRequestedPageSize = 0;
            MaxUpsertManyCount = 0;
        }

        public void Describe(ICapabilities capabilities)
        {
            capabilities
                .Add(DataCaps.Query.Linq)
                .Add(DataCaps.Query.Filter, FilterSupport.Full);
            if (AdvertiseBoundedPaging)
                capabilities.Add(DataCaps.Query.ProviderBoundedPaging);
        }

        public Task<TEntity?> Get(string id, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Current().TryGetValue(id, out var stored) ? Stored(stored) : null);
        }

        public Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<string> ids, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var set = Current();
            return Task.FromResult<IReadOnlyList<TEntity?>>(ids
                .Select(id => set.TryGetValue(id, out var stored) ? Stored(stored) : null).ToArray());
        }

        public Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Current()[model.Id] = Stored(model);
            return Task.FromResult(model);
        }

        // A store keeps its own copy and hands out new instances. Sharing one would let a later in-memory edit (a
        // Load hook's, say) change every partition the instance was written to, which no real adapter does.
        private static TEntity Stored(TEntity model)
            => (TEntity)Koan.Data.Core.Polymorphism.EntityJsonSerialization.DeserializeDocument(
                Koan.Data.Core.Polymorphism.EntityJsonSerialization.SerializeDocument(model), typeof(TEntity));

        public Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
        {
            UpsertManyCalls++;
            var count = 0;
            var set = Current();
            foreach (var model in models)
            {
                ct.ThrowIfCancellationRequested();
                set[model.Id] = Stored(model);
                count++;
            }
            MaxUpsertManyCount = Math.Max(MaxUpsertManyCount, count);
            if (ThrowAfterNextUpsertMany)
            {
                ThrowAfterNextUpsertMany = false;
                throw new InvalidOperationException("injected ambiguous destination fault");
            }
            return Task.FromResult(count);
        }

        public Task<bool> Delete(string id, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Current().TryRemove(id, out _));
        }

        public Task<int> DeleteMany(IEnumerable<string> ids, CancellationToken ct = default)
        {
            var count = 0;
            var set = Current();
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                if (set.TryRemove(id, out _)) count++;
            }
            return Task.FromResult(count);
        }

        public Task<int> DeleteAll(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var set = Current();
            var count = set.Count;
            set.Clear();
            return Task.FromResult(count);
        }

        public async Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
            => await DeleteAll(ct);

        public Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            QueryCalls++;
            MaxRequestedPageSize = Math.Max(MaxRequestedPageSize, query.HasPagination ? query.EffectivePageSize() : 0);
            var before = BeforeNextQuery;
            BeforeNextQuery = null;
            before?.Invoke();
            ct.ThrowIfCancellationRequested();
            IEnumerable<TEntity> selected = Current().Values;
            if (query.Filter is not null)
                selected = selected.Where(InMemoryFilterEvaluator.Compile<TEntity>(query.Filter));

            var filtered = selected.OrderBy(entity => entity.Id, StringComparer.Ordinal).ToArray();
            long? total = query.CountStrategy is null ? null : filtered.Length;
            IEnumerable<TEntity> page = filtered;
            if (query.HasPagination)
                page = page.Skip(query.EffectiveOffset()).Take(query.EffectivePageSize());

            return Task.FromResult(new RepositoryQueryResult<TEntity>
            {
                Items = page.Select(Stored).ToArray(),
                FilterHandled = query.Filter is not null,
                TotalCount = total,
                CountExecution = total is null ? CountExecutionKind.None : CountExecutionKind.Exact,
                PaginationHandled = HandlePagination && query.HasPagination,
                SortHandled = query.Sort.ToFrozenSet()
            });
        }

        public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<TEntity> selected = Current().Values;
            if (query.Filter is not null)
                selected = selected.Where(InMemoryFilterEvaluator.Compile<TEntity>(query.Filter));
            return Task.FromResult(CountResult.Exact(selected.LongCount()));
        }

        public IBatchSet<TEntity, string> CreateBatch() => throw new NotSupportedException();

        private ConcurrentDictionary<string, TEntity> Current()
            => _partitions.GetOrAdd(EntityContext.Current?.Partition ?? "", static _ => new(StringComparer.Ordinal));
    }
}
