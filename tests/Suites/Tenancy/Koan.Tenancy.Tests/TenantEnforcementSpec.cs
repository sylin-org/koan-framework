using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Tenancy.Tests.Support;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// ARCH-0095 P1 / ARCH-0099 §1 — the fail-closed chokepoint gate, proven through a real <c>AddKoan()</c> boot
/// (ARCH-0079) with the <c>Koan.Tenancy</c> module referenced (so its registrar discovers and wires the
/// <c>TenantStorageGuard</c> as a generic <c>IStorageGuard</c>). Exercises the posture (Open / Closed) against
/// tenant-scoped and <c>[HostScoped]</c> entities — proving the data core's generic guard seam carries tenancy
/// purely by registration, and that there is no <c>Off</c> state: the default (no config) in a non-dev host is
/// <b>Closed</b>, secure-by-default.
/// </summary>
public sealed class TenantEnforcementSpec
{
    private static IReadOnlyDictionary<string, string?> Posture(string posture)
        => new Dictionary<string, string?> { ["Koan:Data:Tenancy:Posture"] = posture };

    [Fact]
    public async Task Closed_blocks_a_tenant_scoped_write_with_no_tenant_in_scope()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();

        var act = async () => await ScopedThing.Upsert(new ScopedThing { Title = "x" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No tenant in scope*");
    }

    [Fact]
    public async Task Closed_blocks_a_tenant_scoped_read_with_no_tenant_in_scope()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();

        var act = async () => await ScopedThing.All();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No tenant in scope*");
    }

    [Fact]
    public async Task Closed_allows_the_write_inside_a_tenant_scope()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();

        using (Tenant.Use("t1"))
        {
            var saved = await ScopedThing.Upsert(new ScopedThing { Title = "x" });
            saved.Id.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Closed_allows_a_host_scoped_entity_without_any_tenant()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();

        // [HostScoped] entities opt out of tenant scoping — the quiet, legitimate system exception.
        var saved = await HostThing.Upsert(new HostThing { Title = "x" });

        saved.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Dev_open_allows_a_tenant_scoped_write()
    {
        // Dev-open (a Development host, ARCH-0099 §1): a tenant-scoped op is never blocked — the auto-seeded dev
        // tenant resolves the ambient scope, so the write succeeds with no ceremony.
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(environment: "Development");
        runtime.ResetEntityCaches();

        var saved = await ScopedThing.Upsert(new ScopedThing { Title = "x" });

        saved.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Default_posture_in_a_non_dev_host_is_closed_not_off()
    {
        // No posture config + the Test-env fixture (not Development) → posture derives to Closed. There is no
        // Off state any more (ARCH-0099 §1 retired Mode=Off): the default is secure, a tenant-scoped op with no
        // tenant fails closed.
        await using var runtime = await TenancyRuntimeFixture.CreateAsync();
        runtime.ResetEntityCaches();

        var act = async () => await ScopedThing.Upsert(new ScopedThing { Title = "x" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No tenant in scope*");
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
