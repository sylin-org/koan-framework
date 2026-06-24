using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Containers;
using Xunit;

namespace Koan.Data.Axes.Integration.Tests;

/// <summary>
/// ARCH-0101 §7 (D3) — the decisive BYTE-IDENTICAL behavioral proof. A soft-delete clone authored ENTIRELY through the
/// <see cref="ArchivedAxis"/> premium <c>[DataAxis]</c> layer (discovered + expanded by a real <c>AddKoan()</c> boot,
/// ARCH-0079) passes the SAME scenarios as the hand-written <c>Koan.Data.SoftDelete</c> reference module: <c>Delete</c>
/// soft-removes (hidden from every read site yet physically present under the recycle bin), restore re-shows, hard-delete
/// physically removes, mass-delete soft-removes the visible set — and a NON-<c>[Archived]</c> entity is byte-identical to
/// no-axis (normal physical delete). The expander reproduces the raw seams exactly.
/// </summary>
public sealed class ArchivedAxisEquivalenceSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Archived]
    public sealed class Doc : Entity<Doc> { public string Title { get; set; } = ""; }

    // No [Archived] ⇒ the archived axis is a no-op (AppliesTo is false) ⇒ byte-identical to a vanilla entity.
    public sealed class Plain : Entity<Plain> { public string Title { get; set; } = ""; }

    private static async Task<bool> HardDelete(Doc doc)
    {
        using (OperationOverrideBypass.Enter(typeof(Doc), doc.Id))
        using (ArchivedAmbient.Enter())
            return await doc.Remove();
    }

    [Fact(DisplayName = "[DataAxis] Delete soft-removes: hidden from all reads, but physically present under the recycle bin")]
    public async Task Delete_soft_removes_and_reads_hide_it()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        var a = await new Doc { Title = "a" }.Save();
        var b = await new Doc { Title = "b" }.Save();
        (await Doc.All()).Select(d => d.Title).Should().BeEquivalentTo("a", "b");

        (await Doc.Remove(a.Id)).Should().BeTrue();   // soft-delete (Delete ⇒ __archived = true)

        (await Doc.All()).Select(d => d.Title).Should().Equal("b");
        (await Doc.Get(a.Id)).Should().BeNull();
        (await Data<Doc, string>.Count(QueryDefinition.All)).Should().Be(1);

        using (Archived.WithDeleted()) (await Doc.All()).Select(d => d.Title).Should().BeEquivalentTo("a", "b");
        using (Archived.WithDeleted()) (await Doc.Get(a.Id)).Should().NotBeNull();
        using (Archived.WithDeleted()) (await Data<Doc, string>.Count(QueryDefinition.All)).Should().Be(2);
    }

    [Fact(DisplayName = "[DataAxis] Restore makes an archived row visible again")]
    public async Task Restore_makes_an_archived_row_visible_again()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        var a = await new Doc { Title = "a" }.Save();
        await Doc.Remove(a.Id);
        (await Doc.All()).Should().BeEmpty();

        Doc loaded;
        using (Archived.WithDeleted()) loaded = (await Doc.Get(a.Id))!;
        loaded.Should().NotBeNull();
        await loaded.Save();   // a normal save no longer stamps __archived ⇒ visible again

        (await Doc.All()).Select(d => d.Title).Should().Equal("a");
    }

    [Fact(DisplayName = "[DataAxis] HardDelete physically removes the row (gone even under the recycle bin)")]
    public async Task HardDelete_physically_removes()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        var a = await new Doc { Title = "a" }.Save();
        (await HardDelete(a)).Should().BeTrue();

        using (Archived.WithDeleted()) (await Doc.Get(a.Id)).Should().BeNull();
        using (Archived.WithDeleted()) (await Doc.All()).Should().BeEmpty();
    }

    [Fact(DisplayName = "[DataAxis] HardDelete purges an ALREADY-archived row (empties the recycle bin)")]
    public async Task HardDelete_purges_an_already_archived_row()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        var a = await new Doc { Title = "a" }.Save();
        await Doc.Remove(a.Id);
        using (Archived.WithDeleted()) (await Doc.Get(a.Id)).Should().NotBeNull();

        (await HardDelete(a)).Should().BeTrue();
        using (Archived.WithDeleted()) (await Doc.Get(a.Id)).Should().BeNull();
    }

    [Fact(DisplayName = "[DataAxis] DeleteAll / RemoveAll soft-remove every visible row (not a physical truncate)")]
    public async Task Mass_delete_soft_removes_all_visible()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        await new Doc { Title = "a" }.Save();
        await new Doc { Title = "b" }.Save();

        (await Data<Doc, string>.DeleteAll()).Should().Be(2);
        (await Doc.All()).Should().BeEmpty();
        using (Archived.WithDeleted()) (await Doc.All()).Should().HaveCount(2);

        await new Doc { Title = "c" }.Save();
        await Doc.RemoveAll();
        (await Doc.All()).Should().BeEmpty();
        using (Archived.WithDeleted()) (await Doc.All()).Should().HaveCount(3);
    }

    [Fact(DisplayName = "[DataAxis] a NON-applicable entity is byte-identical to no-axis (normal physical delete)")]
    public async Task A_non_applicable_entity_is_byte_identical()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        var a = await new Plain { Title = "a" }.Save();
        (await Plain.Remove(a.Id)).Should().BeTrue();   // physical remove — the archived axis does not apply

        (await Plain.Get(a.Id)).Should().BeNull();
        using (Archived.WithDeleted()) (await Plain.Get(a.Id)).Should().BeNull();   // genuinely gone (not soft)
        (await Data<Plain, string>.Count(QueryDefinition.All)).Should().Be(0);
    }
}
