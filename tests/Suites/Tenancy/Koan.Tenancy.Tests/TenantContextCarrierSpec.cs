using System;
using AwesomeAssertions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// ARCH-0100 phase 4 — the tenancy <see cref="IAmbientSliceCarrier"/>. Captures the tri-state ambient tenant slice
/// to a versioned portable string at submit and rehydrates it at execute, reusing <c>Tenant.Use</c>/<c>Tenant.None</c>
/// (which already return the restore scope). Pins the round-trip, the host-vs-unset distinction surviving the hop,
/// and versioned fail-closed (an unknown format dead-letters rather than mis-restoring).
/// </summary>
public sealed class TenantContextCarrierSpec
{
    private static readonly IAmbientSliceCarrier Carrier = new TenantContextCarrier();

    [Fact]
    public void Axis_key_is_the_stable_tenant_key()
        => Carrier.AxisKey.Should().Be("koan:tenant");

    [Fact]
    public void Capture_is_null_when_no_tenant_is_in_scope()
        => Carrier.Capture().Should().BeNull();

    [Fact]
    public void Capture_then_Restore_round_trips_a_concrete_tenant_across_a_hop()
    {
        string? bag;
        using (Tenant.Use("acme")) bag = Carrier.Capture();   // submit-side
        bag.Should().NotBeNull();

        Tenant.Current.Should().BeNull();                     // the hop lost the slice
        using (Carrier.Restore(bag!))                         // execute-side
        {
            Tenant.Current!.HasTenant.Should().BeTrue();
            Tenant.Current!.Id.Should().Be("acme");
        }
        Tenant.Current.Should().BeNull();
    }

    [Fact]
    public void Explicit_host_scope_and_unset_survive_distinctly()
    {
        string? hostBag;
        using (Tenant.None()) hostBag = Carrier.Capture();
        Carrier.Capture().Should().BeNull();                  // unset captures nothing...
        hostBag.Should().NotBeNull();                         // ...but explicit host scope does

        using (Carrier.Restore(hostBag!))
        {
            Tenant.Current!.IsHost.Should().BeTrue();         // restored as explicit host, NOT unset
            Tenant.Current!.HasTenant.Should().BeFalse();
        }
    }

    [Fact]
    public void Restore_fails_closed_on_an_unknown_version()
    {
        // A bag written by a future carrier format must dead-letter (named), never silently mis-restore.
        var act = () => Carrier.Restore("v999:whatever");
        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("tenant");
    }
}
