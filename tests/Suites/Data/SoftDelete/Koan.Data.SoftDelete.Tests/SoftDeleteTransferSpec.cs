using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transfers;
using Koan.Testing.Containers;
using Xunit;

namespace Koan.Data.SoftDelete.Tests;

/// <summary>
/// NK-184 (DATA-0155) on the real SQLite adapter. A transfer reads what the caller sees, so a soft-deleted row travels
/// only inside <c>WithDeleted()</c>, and then it must arrive soft-deleted: it used to arrive visible. A Move removes the
/// rows it moved: it used to leave each one in the origin as a tombstone and still count it as deleted.
/// </summary>
public sealed class SoftDeleteTransferSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [SoftDelete]
    public sealed class Card : Entity<Card> { public string Title { get; set; } = ""; }

    [Fact(DisplayName = "NK-184: a Copy inside WithDeleted keeps a soft-deleted row soft-deleted in the destination")]
    public async Task Copy_inside_WithDeleted_keeps_soft_deleted_rows_hidden()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var origin = NewPartition("origin");
        var archive = NewPartition("archive");

        using (Lease(origin))
        {
            await new Card { Id = "live", Title = "live" }.Save();
            await new Card { Id = "dead", Title = "dead" }.Save();
            await Card.Remove("dead");

            (await Card.Copy().To(partition: archive).Run()).CopiedCount.Should().Be(1, "a plain Copy reads what the caller sees");
            using (Card.WithDeleted())
                (await Card.Copy().To(partition: archive).Run()).CopiedCount.Should().Be(2);
        }

        using (Lease(archive))
        {
            (await Card.All()).Select(card => card.Id).Should().Equal("live");
            using (Card.WithDeleted())
                (await Card.All()).Select(card => card.Id).Should().BeEquivalentTo(["live", "dead"]);
        }
    }

    [Fact(DisplayName = "NK-184: a Move removes what it moved, and inside WithDeleted carries tombstones as tombstones")]
    public async Task Move_removes_what_it_moved_and_carries_tombstones_inside_WithDeleted()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var origin = NewPartition("origin");
        var archive = NewPartition("archive");
        var bin = NewPartition("bin");

        using (Lease(origin))
        {
            await new Card { Id = "live", Title = "live" }.Save();
            await new Card { Id = "dead", Title = "dead" }.Save();
            await Card.Remove("dead");

            var moved = await Card.Move().To(partition: archive).Run();
            moved.DeletedCount.Should().Be(1);
            moved.Warnings.Should().BeEmpty();
            using (Card.WithDeleted())
                (await Card.All()).Select(card => card.Id).Should().Equal(["dead"], "the moved row left the origin; the tombstone was not selected");

            using (Card.WithDeleted())
            {
                var emptied = await Card.Move().To(partition: bin).Run();
                emptied.CopiedCount.Should().Be(1);
                emptied.DeletedCount.Should().Be(1);
                (await Card.All()).Should().BeEmpty("the tombstone left the origin too");
            }
        }

        using (Lease(archive)) (await Card.All()).Select(card => card.Id).Should().Equal("live");
        using (Lease(bin))
        {
            (await Card.All()).Should().BeEmpty("the tombstone arrived soft-deleted");
            using (Card.WithDeleted()) (await Card.All()).Select(card => card.Id).Should().Equal("dead");
        }
    }

    [Fact(DisplayName = "NK-184: a Semantic Move removes each row as a Remove does, and says the origin keeps tombstones")]
    public async Task Semantic_move_soft_deletes_the_origin_and_reports_it()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var origin = NewPartition("origin");
        var archive = NewPartition("archive");

        using (Lease(origin))
        {
            await new Card { Id = "live", Title = "live" }.Save();

            var moved = await Card.Move().To(partition: archive).Fidelity(TransferFidelity.Semantic).Run();
            moved.DeletedCount.Should().Be(1);
            moved.Warnings.Should().ContainSingle(warning => warning.Contains("tombstone"));
            (await Card.All()).Should().BeEmpty();
            using (Card.WithDeleted()) (await Card.All()).Select(card => card.Id).Should().Equal("live");
        }
    }
}
