using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Containers;
using Xunit;

namespace Koan.Data.Axes.Integration.Tests;

/// <summary>
/// ARCH-0101 §7 (D5) — two expander-produced axes compose on ONE entity exactly like hand-written ones. A
/// <c>[Regional][Archived] Doc</c> carries the equality <see cref="RegionAxis"/> (<c>__region</c>, auto-equality fold)
/// AND the predicate <see cref="ArchivedAxis"/> (<c>__archived</c>, hide-archived). A read is AND-scoped by BOTH: a row
/// is invisible if it belongs to another region OR has been archived. Proven through a real <c>AddKoan()</c> boot
/// (ARCH-0079) — the equality + predicate read fold the design review flagged as the composition gap.
/// </summary>
public sealed class MultiAxisCompositionSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Regional]
    [Archived]
    public sealed class Doc : Entity<Doc> { public string Title { get; set; } = ""; }

    [Fact(DisplayName = "region equality + archived predicate fold into one read together")]
    public async Task Two_axes_scope_one_read()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        Doc x, y;
        using (RegionAmbient.Use("r1")) x = await new Doc { Title = "x" }.Save();
        using (RegionAmbient.Use("r2")) y = await new Doc { Title = "y" }.Save();

        // Region equality scopes each read to its own region's rows.
        using (RegionAmbient.Use("r1")) (await Doc.All()).Select(d => d.Title).Should().Equal("x");
        using (RegionAmbient.Use("r2")) (await Doc.All()).Select(d => d.Title).Should().Equal("y");

        // Soft-delete x under r1: the archived predicate now ALSO hides it — both axes fold into the read.
        using (RegionAmbient.Use("r1")) (await Doc.Remove(x.Id)).Should().BeTrue();
        using (RegionAmbient.Use("r1")) (await Doc.All()).Should().BeEmpty();                       // hidden by archived
        using (RegionAmbient.Use("r1")) using (Archived.WithDeleted())
            (await Doc.All()).Select(d => d.Title).Should().Equal("x");                              // recycle bin, still region-scoped

        // r2 is untouched by either axis applied under r1.
        using (RegionAmbient.Use("r2")) (await Doc.All()).Select(d => d.Title).Should().Equal("y");

        // Cross-region: even the recycle bin cannot reach another region's archived row (region equality holds).
        using (RegionAmbient.Use("r2")) using (Archived.WithDeleted())
            (await Doc.Get(x.Id)).Should().BeNull();
    }
}
