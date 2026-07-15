using System;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// Pins tenancy's Core-owned context carrier: stable wire identity and encoding, authenticated ingress requirement,
/// tri-state round-trip, explicit suppression, safe typed refusals, and independent module registration.
/// </summary>
public sealed class TenantContextCarrierSpec
{
    private static readonly IKoanContextCarrier Carrier = new TenantContextCarrier();

    [Fact]
    public void Axis_key_is_the_stable_tenant_key()
        => Carrier.AxisKey.Should().Be("koan:tenant");

    [Fact]
    public void Tenant_context_requires_authenticated_ingress()
        => Carrier.MinimumIngressTrust.Should().Be(ContextIngressTrust.Authenticated);

    [Fact]
    public void Capture_is_null_when_no_tenant_is_in_scope()
        => Carrier.Capture().Should().BeNull();

    [Fact]
    public void Capture_then_Restore_round_trips_a_concrete_tenant_across_a_hop()
    {
        string? bag;
        using (Tenant.Use("acme")) bag = Carrier.Capture();   // submit-side
        bag.Should().Be("v1:id:acme");

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
        hostBag.Should().Be("v1:host");                     // ...but explicit host scope does

        using (Carrier.Restore(hostBag!))
        {
            Tenant.Current!.IsHost.Should().BeTrue();         // restored as explicit host, NOT unset
            Tenant.Current!.HasTenant.Should().BeFalse();
        }
    }

    [Fact]
    public void Suppress_clears_worker_context_then_restores_it()
    {
        using (Tenant.Use("worker"))
        {
            using (Carrier.Suppress())
                Tenant.Current.Should().BeNull();

            Tenant.Current!.Id.Should().Be("worker");
        }
    }

    [Theory]
    [InlineData("v1:id:", KoanContextCarrierException.FailureKind.MalformedPayload)]
    [InlineData("v1:unknown", KoanContextCarrierException.FailureKind.MalformedPayload)]
    [InlineData("v999:private-payload", KoanContextCarrierException.FailureKind.UnsupportedVersion)]
    public void Restore_fails_closed_with_safe_typed_errors(
        string captured,
        KoanContextCarrierException.FailureKind expected)
    {
        var act = () => Carrier.Restore(captured);

        var failure = act.Should().Throw<KoanContextCarrierException>().Which;
        failure.Failure.Should().Be(expected);
        failure.AxisKeys.Should().Equal("koan:tenant");
        failure.Message.Should().NotContain(captured);
        failure.InnerException.Should().BeNull();
        Tenant.Current.Should().BeNull();
    }

    [Fact]
    public void Tenancy_module_registers_its_carrier_independently_with_Core()
    {
        var services = new ServiceCollection();
        services.AddKoanCore();
        new Initialization.KoanAutoRegistrar().Register(services);
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IKoanContextCarrier>()
            .Should().ContainSingle().Which.Should().BeOfType<TenantContextCarrier>();
    }
}
