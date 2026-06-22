using System.Collections.Generic;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Tenancy;
using Koan.Tests.Data.Core.Support;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Tenancy;

/// <summary>
/// ARCH-0095 slice 1b — the fail-closed chokepoint gate (P1), proven through a real <c>AddKoan()</c> boot
/// (ARCH-0079) on the no-Docker JSON adapter. Exercises the activation gradient (Off / Warn / Enforce)
/// against tenant-scoped and <c>[HostScoped]</c> entities. The read-filter and write-stamp (the actual
/// no-leak proof, P7) land in slice 1c; this slice proves the gate itself.
/// </summary>
public sealed class TenantEnforcementSpec
{
    private static IReadOnlyDictionary<string, string?> Mode(string mode)
        => new Dictionary<string, string?> { ["Koan:Data:Tenancy:Mode"] = mode };

    [Fact]
    public async Task Enforce_blocks_a_tenant_scoped_write_with_no_tenant_in_scope()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(extraSettings: Mode("Enforce"));
        runtime.ResetEntityCaches();

        var act = async () => await ScopedThing.Upsert(new ScopedThing { Title = "x" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No tenant in scope*");
    }

    [Fact]
    public async Task Enforce_blocks_a_tenant_scoped_read_with_no_tenant_in_scope()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(extraSettings: Mode("Enforce"));
        runtime.ResetEntityCaches();

        var act = async () => await ScopedThing.All();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No tenant in scope*");
    }

    [Fact]
    public async Task Enforce_allows_the_write_inside_a_tenant_scope()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(extraSettings: Mode("Enforce"));
        runtime.ResetEntityCaches();

        using (Tenant.Use("t1"))
        {
            var saved = await ScopedThing.Upsert(new ScopedThing { Title = "x" });
            saved.Id.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Enforce_allows_a_host_scoped_entity_without_any_tenant()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(extraSettings: Mode("Enforce"));
        runtime.ResetEntityCaches();

        // [HostScoped] entities opt out of tenant scoping — the quiet, legitimate system exception.
        var saved = await HostThing.Upsert(new HostThing { Title = "x" });

        saved.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Warn_allows_a_tenant_scoped_write_with_no_tenant_in_scope()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(extraSettings: Mode("Warn"));
        runtime.ResetEntityCaches();

        // Warn logs the fix but does NOT block (the activation gradient's soft step).
        var saved = await ScopedThing.Upsert(new ScopedThing { Title = "x" });

        saved.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Off_by_default_does_not_gate_a_tenant_scoped_entity()
    {
        // No tenancy config → Mode=Off → a non-tenant app behaves identically (zero regression).
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        runtime.ResetEntityCaches();

        var saved = await ScopedThing.Upsert(new ScopedThing { Title = "x" });

        saved.Id.Should().NotBeNullOrEmpty();
    }

    private sealed class ScopedThing : Entity<ScopedThing, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
        public string Title { get; set; } = "";
    }

    [HostScoped]
    private sealed class HostThing : Entity<HostThing, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
        public string Title { get; set; } = "";
    }
}
