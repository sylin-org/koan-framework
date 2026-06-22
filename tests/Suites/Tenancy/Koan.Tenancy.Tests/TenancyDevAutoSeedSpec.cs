using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Tenancy;
using Koan.Tenancy.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// ARCH-0099 §1 — the dev auto-seed WIRING, through a real <c>AddKoan()</c> boot (ARCH-0079). Under Open posture
/// the module seeds an in-memory dev tenant + Owner membership + branded key, and an unset ambient scope falls
/// back to that dev tenant (so a developer's ops land in it with no day-one 403, yet are still a real, isolated
/// tenant scope). Under Closed posture nothing is seeded and there is no fallback.
/// </summary>
public sealed class TenancyDevAutoSeedSpec
{
    // Dev-open is reached by booting a Development host (per-host IHostEnvironment), not by forcing the posture in
    // a non-dev env — a forced Open outside Development is refused by the boot pre-flight (ARCH-0099 §1).
    private static IReadOnlyDictionary<string, string?> DevUser(string devUser = "leo@acme.dev")
        => new Dictionary<string, string?> { ["Koan:Data:Tenancy:DevUser"] = devUser };

    private static IDisposable Isolate() => EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

    public sealed class Note : Entity<Note> { public string Title { get; set; } = ""; }

    [Fact]
    public async Task Open_host_seeds_a_dev_tenant_owner_and_branded_key()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: DevUser(), environment: "Development");
        runtime.ResetEntityCaches();

        var state = runtime.Services.GetRequiredService<TenancyDevState>();
        state.IsSeeded.Should().BeTrue();
        state.DevTenantId.Should().Be(TenancyDevSeed.DevTenantId);
        state.DevTenantName.Should().Be("Acme");
        state.OwnerIdentityId.Should().Be("leo@acme.dev");
        state.OwnerRole.Should().Be("koan:owner");
        state.SigningKey.Should().StartWith(TenancyDevBrand.Prefix);
        state.FallbackTenantId.Should().Be(state.DevTenantId);
    }

    [Fact]
    public async Task Closed_host_does_not_seed_and_has_no_fallback()
    {
        // The default Test environment is non-Development → posture Closed → no seed.
        await using var runtime = await TenancyRuntimeFixture.CreateAsync();
        runtime.ResetEntityCaches();

        var state = runtime.Services.GetRequiredService<TenancyDevState>();
        state.IsSeeded.Should().BeFalse();
        state.FallbackTenantId.Should().BeNull();
    }

    [Fact]
    public async Task Open_unscoped_op_resolves_to_the_dev_tenant_and_isolates_from_other_tenants()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: DevUser(), environment: "Development");
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
    public async Task Open_host_under_Tenant_None_is_host_scope_not_the_dev_fallback()
    {
        // Tenant.None() is the explicit host escape — it must DEFEAT the dev fallback (the slice-wins short-circuit
        // in TenancyAmbient), so a tenant-scoped op under None() is host-scoped (unstamped), never dev-stamped. If
        // None() leaked to the dev fallback, the host row would carry the dev stamp and show up in the dev read.
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: DevUser(), environment: "Development");
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        var devRow = await new Note { Title = "dev" }.Save();           // un-scoped → dev fallback (stamped "dev")
        Note hostRow;
        using (Tenant.None()) hostRow = await new Note { Title = "host" }.Save(); // host scope (unstamped)
        hostRow.Id.Should().NotBeNullOrEmpty();

        // The dev-fallback read sees its own dev row but NOT the host-scope row — None() did not stamp it "dev".
        (await Note.All()).Select(n => n.Id).Should().Contain(devRow.Id).And.NotContain(hostRow.Id);
    }
}
