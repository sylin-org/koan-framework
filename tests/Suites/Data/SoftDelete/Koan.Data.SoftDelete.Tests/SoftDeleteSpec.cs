using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.SoftDelete;
using Koan.Testing.Containers;
using Xunit;

namespace Koan.Data.SoftDelete.Tests;

/// <summary>
/// ARCH-0101 §4 — the operation-semantics override plane, proven end-to-end on the real SQLite adapter via the
/// canonical <c>Koan.Data.SoftDelete</c> reference module (real <c>AddKoan()</c> boot, ARCH-0079). A <c>[SoftDelete]</c>
/// entity rides pure contributors: <c>Delete</c> sets an invisible <c>__deleted</c> field (operation override) instead
/// of physically removing the row, reads hide deleted rows (DATA-0106 read contributor), and the escape verbs
/// <c>.HardDelete()</c> / <c>.Restore()</c> / <c>T.WithDeleted()</c> work. The decisive proofs: a soft-deleted row is
/// HIDDEN from every read site yet PHYSICALLY PRESENT (visible under <c>WithDeleted()</c>); a hard-delete physically
/// removes it; restore makes it visible again.
/// </summary>
public sealed class SoftDeleteSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [SoftDelete]
    public sealed class Doc : Entity<Doc> { public string Title { get; set; } = ""; }

    [Fact(DisplayName = "Delete soft-removes: hidden from all reads, but physically present (visible under WithDeleted)")]
    public async Task Delete_soft_removes_and_reads_hide_it()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        var a = await new Doc { Title = "a" }.Save();
        var b = await new Doc { Title = "b" }.Save();
        (await Doc.All()).Select(d => d.Title).Should().BeEquivalentTo("a", "b");

        (await Doc.Remove(a.Id)).Should().BeTrue();   // soft-delete (Delete ⇒ __deleted=true)

        // Hidden from Query/All, get-by-id, and Count.
        (await Doc.All()).Select(d => d.Title).Should().Equal("b");
        (await Doc.Get(a.Id)).Should().BeNull();
        (await Data<Doc, string>.Count(QueryDefinition.All)).Should().Be(1);

        // But PHYSICALLY present (soft, not hard) — the recycle bin shows it.
        using (Doc.WithDeleted()) (await Doc.All()).Select(d => d.Title).Should().BeEquivalentTo("a", "b");
        using (Doc.WithDeleted()) (await Doc.Get(a.Id)).Should().NotBeNull();
        using (Doc.WithDeleted()) (await Data<Doc, string>.Count(QueryDefinition.All)).Should().Be(2);
    }

    [Fact(DisplayName = "Restore makes a soft-deleted row visible again")]
    public async Task Restore_makes_a_soft_deleted_row_visible_again()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        var a = await new Doc { Title = "a" }.Save();
        await Doc.Remove(a.Id);
        (await Doc.All()).Should().BeEmpty();

        Doc loaded;
        using (Doc.WithDeleted()) loaded = (await Doc.Get(a.Id))!;
        loaded.Should().NotBeNull();
        await loaded.Restore();

        (await Doc.All()).Select(d => d.Title).Should().Equal("a");   // visible again
    }

    [Fact(DisplayName = ".HardDelete() physically removes the row (gone even under WithDeleted)")]
    public async Task HardDelete_physically_removes()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        var a = await new Doc { Title = "a" }.Save();
        (await a.HardDelete()).Should().BeTrue();

        using (Doc.WithDeleted()) (await Doc.Get(a.Id)).Should().BeNull();   // physically gone
        using (Doc.WithDeleted()) (await Doc.All()).Should().BeEmpty();
    }

    [Fact(DisplayName = ".HardDelete() purges an ALREADY-soft-deleted row (empties the recycle bin)")]
    public async Task HardDelete_purges_an_already_soft_deleted_row()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        var a = await new Doc { Title = "a" }.Save();
        await Doc.Remove(a.Id);                       // soft-delete first
        using (Doc.WithDeleted()) (await Doc.Get(a.Id)).Should().NotBeNull();   // in the recycle bin

        (await a.HardDelete()).Should().BeTrue();     // purge it (HardDelete enters WithDeleted to see it)
        using (Doc.WithDeleted()) (await Doc.Get(a.Id)).Should().BeNull();      // physically gone
    }

    [Fact(DisplayName = "DeleteAll / RemoveAll soft-remove every visible row (not a physical truncate)")]
    public async Task Mass_delete_soft_removes_all_visible()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        await new Doc { Title = "a" }.Save();
        await new Doc { Title = "b" }.Save();

        (await Data<Doc, string>.DeleteAll()).Should().Be(2);
        (await Doc.All()).Should().BeEmpty();
        using (Doc.WithDeleted()) (await Doc.All()).Should().HaveCount(2);   // still physically present

        await new Doc { Title = "c" }.Save();
        await Doc.RemoveAll();
        (await Doc.All()).Should().BeEmpty();
        using (Doc.WithDeleted()) (await Doc.All()).Should().HaveCount(3);
    }

    [Fact(DisplayName = "Batch delete preserves soft-delete semantics")]
    public async Task Batch_delete_soft_removes_instead_of_physically_deleting()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        var doc = await new Doc { Title = "batched" }.Save();
        var result = await Doc.Batch().Delete(doc.Id).Save();

        result.Deleted.Should().Be(1);
        (await Doc.Get(doc.Id)).Should().BeNull();
        using (Doc.WithDeleted()) (await Doc.Get(doc.Id)).Should().NotBeNull();
    }

    [Fact(DisplayName = "Atomic batch delete fails closed when soft-delete requires multiple writes")]
    public async Task Atomic_batch_delete_does_not_claim_false_atomicity()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        var doc = await new Doc { Title = "atomic" }.Save();
        var action = () => Doc.Batch().Delete(doc.Id).Save(new BatchOptions(RequireAtomic: true));

        await action.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Atomic batch removal*soft-deleted entity*");
        (await Doc.Get(doc.Id)).Should().NotBeNull("the contract is rejected before the first write");
    }
}
