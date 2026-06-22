using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// Pins the ambient <c>Tenant</c> slice (ARCH-0095 slice 1a) — the flagship typed slice of the Facet-3 ambient
/// carrier. It rides the ONE <see cref="EntityContext"/> carrier via the generic slice API (ARCH-0097), is
/// immutable and restore-on-dispose, and models the three states tenancy needs: <b>unset</b> (no tenant in
/// scope), <b>host</b> (the loud <see cref="Tenant.None"/> escape), and <b>scoped</b>
/// (<see cref="Tenant.Use"/> / <see cref="Tenant.WithTenant"/>). Enforcement is a separate spec.
/// </summary>
public class TenantAmbientSpec
{
    [Fact]
    public void Current_is_null_when_no_tenant_is_in_scope()
    {
        Tenant.Current.Should().BeNull();
    }

    [Fact]
    public void WithTenant_scopes_a_tenant_then_restores_on_dispose()
    {
        Tenant.Current.Should().BeNull();

        using (Tenant.WithTenant("a1b2c3"))
        {
            Tenant.Current.Should().NotBeNull();
            Tenant.Current!.Id.Should().Be("a1b2c3");
            Tenant.Current!.IsHost.Should().BeFalse();
            Tenant.Current!.HasTenant.Should().BeTrue();
        }

        Tenant.Current.Should().BeNull(); // restored to unset
    }

    [Fact]
    public void Use_is_a_synonym_for_WithTenant()
    {
        using (Tenant.Use("a1b2c3"))
            Tenant.Current!.Id.Should().Be("a1b2c3");
    }

    [Fact]
    public void None_sets_an_explicit_host_scope_distinct_from_unset()
    {
        using (Tenant.None())
        {
            Tenant.Current.Should().NotBeNull();
            Tenant.Current!.IsHost.Should().BeTrue();
            Tenant.Current!.Id.Should().BeNull();
            Tenant.Current!.HasTenant.Should().BeFalse();
        }

        Tenant.Current.Should().BeNull();
    }

    [Fact]
    public void Nested_use_shadows_then_restores_the_parent()
    {
        using (Tenant.Use("outer"))
        {
            Tenant.Current!.Id.Should().Be("outer");
            using (Tenant.Use("inner"))
                Tenant.Current!.Id.Should().Be("inner");
            Tenant.Current!.Id.Should().Be("outer"); // parent restored, not nulled
        }
    }

    [Fact]
    public void Tenant_carries_across_an_unrelated_entitycontext_scope()
    {
        // Changing another axis (partition) must NOT drop the tenant — inherit-unless-overridden (ARCH-0097).
        using (Tenant.Use("a1b2c3"))
        using (EntityContext.With(partition: "archive"))
        {
            Tenant.Current!.Id.Should().Be("a1b2c3");
            EntityContext.Current!.Partition.Should().Be("archive");
        }
    }

    [Fact]
    public void None_overrides_an_inherited_tenant_then_restores()
    {
        using (Tenant.Use("a1b2c3"))
        {
            using (Tenant.None())
                Tenant.Current!.IsHost.Should().BeTrue();
            Tenant.Current!.Id.Should().Be("a1b2c3"); // restored to the scoped parent
        }
    }

    [Fact]
    public async Task Parallel_async_flows_do_not_clobber_each_others_tenant()
    {
        async Task Body(string id)
        {
            using (Tenant.Use(id))
            {
                await Task.Yield();
                await Task.Delay(5);
                Tenant.Current!.Id.Should().Be(id);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Body($"t{i}")));
        Tenant.Current.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithTenant_rejects_a_null_or_blank_tenant_id(string? id)
    {
        var act = () => Tenant.WithTenant(id!);
        act.Should().Throw<ArgumentException>();
    }
}
