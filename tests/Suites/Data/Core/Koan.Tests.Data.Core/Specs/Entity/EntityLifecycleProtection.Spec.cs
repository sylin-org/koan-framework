using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Lifecycle;
using Koan.Data.Core.Model;
using Koan.Tests.Data.Core.Support;

namespace Koan.Tests.Data.Core.Specs.Entity;

/// <summary>
/// NK-014 (DATA-0132): protection guards handlers, not the framework's own write stamps. After the write, protection
/// is rebased to what was persisted (minted identity, [Timestamp] stamps), so an after-handler is checked against the
/// stored row and a committed write never throws for the framework's own changes.
/// </summary>
public sealed class EntityLifecycleProtectionSpec
{
    [Fact]
    public async Task ProtectAll_tolerates_the_identity_and_timestamp_the_write_stamps()
    {
        var after = 0;
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
            StampedDoc.Lifecycle
                .BeforeUpsert(ctx => { ctx.ProtectAll(); return ctx.Proceed(); })
                .AfterUpsert(_ => after++));
        using var partition = runtime.UsePartition($"lifecycle-protect-{Guid.CreateVersion7():n}");

        var saved = await StampedDoc.Upsert(new StampedDoc { Title = "x" });
        saved.Id.Should().NotBeNullOrWhiteSpace();
        var batch = await StampedDoc.Batch().Add(new StampedDoc { Title = "y" }).Save();

        after.Should().Be(2);
        batch.Added.Should().Be(1);
        (await StampedDoc.All(partition.Partition)).Should().HaveCount(2);
    }

    [Fact]
    public async Task An_after_handler_that_mutates_a_protected_field_still_throws()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
            StampedDoc.Lifecycle
                .BeforeUpsert(ctx => { ctx.ProtectAll(); return ctx.Proceed(); })
                .AfterUpsert(ctx => ctx.Current.Title = "changed after the write"));
        using var partition = runtime.UsePartition($"lifecycle-protect-{Guid.CreateVersion7():n}");

        var save = () => StampedDoc.Upsert(new StampedDoc { Title = "x" });
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Title*protected*");
    }

    private sealed class StampedDoc : Entity<StampedDoc, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
        public string Title { get; set; } = "";
        [Timestamp(OnSave = true)]
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
