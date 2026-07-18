using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Tenancy;
using Koan.Tenancy.Tests.Support;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// The Development local fallback through a real <c>AddKoan()</c> boot. Open posture gives unscoped work one stable,
/// isolated local tenant; explicit host and tenant scopes still win.
/// </summary>
public sealed class TenancyDevAutoSeedSpec
{
    private static IDisposable Isolate() => EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

    public sealed class Note : Entity<Note> { public string Title { get; set; } = ""; }

    [Fact]
    public async Task Open_unscoped_op_resolves_to_the_dev_tenant_and_isolates_from_other_tenants()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(environment: "Development");
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        // A write under an explicit OTHER tenant is invisible to the un-scoped (dev-fallback) read.
        Note other;
        using (Tenant.Use("other")) other = await new Note { Title = "other-secret" }.Save();

        var devVisible = await Note.All(); // un-scoped → falls back to the dev tenant
        devVisible.Select(n => n.Id).Should().NotContain(other.Id);

        // An un-scoped write lands under the dev tenant and IS visible to the un-scoped read.
        var mine = await new Note { Title = "mine" }.Save();
        (await Note.All()).Select(n => n.Id).Should().Contain(mine.Id);

        // And the dev row is invisible to the explicit OTHER tenant — the fallback is a real, isolated scope.
        using (Tenant.Use("other")) (await Note.Get(mine.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Open_host_under_Tenant_None_defeats_the_fallback_and_fails_tenant_entity_writes()
    {
        // Tenant.None() is the explicit host escape — it must DEFEAT the dev fallback (the slice-wins short-circuit
        // in TenancyAmbient). A tenant-scoped Entity is still not host data, so its write must fail rather than become
        // dev-stamped or silently unsegmented.
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(environment: "Development");
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        var devRow = await new Note { Title = "dev" }.Save();           // un-scoped → dev fallback (stamped "dev")
        using (Tenant.None())
        {
            var act = async () => await new Note { Title = "host" }.Save();
            await act.Should().ThrowAsync<SegmentationRequiredException>();
        }

        (await Note.All()).Select(n => n.Id).Should().ContainSingle().Which.Should().Be(devRow.Id);
    }
}
