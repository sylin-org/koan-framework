using System;
using System.Threading;
using AwesomeAssertions;
using Koan.Core.Naming;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Axes.Tests.Support;
using Koan.Data.Core.Axes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Axes.Tests;

/// <summary>
/// ARCH-0101 §3/§7 (D4) — a <see cref="AxisMode.Container"/> <see cref="Axis"/> expands to an
/// <c>IStorageNameParticleContributor</c> that is byte-identical to the hand-written <c>TenantNameParticle</c> fixture
/// (the legit oracle — no production container-mode ships yet): a LEADING particle wraps the anchor (<c>T1-base</c>),
/// the partition still TRAILS (<c>#alpha</c>), no axis in scope is byte-identical, the axis value is in the name cache
/// key (two tenants ⇒ distinct names — the cross-container security pin), and a lossy value fails closed (inherited
/// from <c>StorageNameGenerator</c>).
/// </summary>
public sealed class ContainerModeSpec : IDisposable
{
    private static readonly AsyncLocal<string?> _tenant = new();

    public ContainerModeSpec() => AxisRegistries.ResetAll();
    public void Dispose() { _tenant.Value = null; AxisRegistries.ResetAll(); }

    private sealed class NamedDoc { }
    private sealed class OtherDoc { }

    private static void RegisterTenantContainerAxis()
        => DataAxisExpander.ExpandAxes(new[]
        {
            new Axis()
                .Named("tenant").Mode(AxisMode.Container)
                .AppliesTo(t => t == typeof(NamedDoc))
                .Field("__tenant", () => _tenant.Value),
        }, new ServiceCollection());

    private static IDisposable Tenant(string id)
    {
        var prev = _tenant.Value;
        _tenant.Value = id;
        return new Pop(() => _tenant.Value = prev);
    }

    private sealed class Pop(Action undo) : IDisposable { public void Dispose() => undo(); }

    private static StorageNamingCapability Cap() => new() { NameOverride = _ => "base" };
    private static StorageNamingCapability LowercaseCap() => new() { NameOverride = _ => "base", Partition = new PartitionTokenPolicy { Lowercase = true } };

    [Fact]
    public void A_leading_axis_particle_wraps_the_anchor_and_the_partition_still_trails()
    {
        RegisterTenantContainerAxis();
        using (Tenant("T1"))
            StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", Cap()).Should().Be("T1-base#alpha");
    }

    [Fact]
    public void No_axis_in_scope_is_byte_identical()
    {
        RegisterTenantContainerAxis();
        StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", Cap()).Should().Be("base#alpha");
    }

    [Fact]
    public void A_non_applicable_type_is_byte_identical()
    {
        RegisterTenantContainerAxis();
        using (Tenant("T1"))
            StorageNameGenerator.Generate(typeof(OtherDoc), "alpha", Cap()).Should().Be("base#alpha");
    }

    [Fact]
    public void The_axis_value_is_in_the_name_cache_key_so_two_tenants_get_distinct_names()
    {
        RegisterTenantContainerAxis();
        const string provider = "container-mode-spec";   // unique provider isolates the static cache

        string n1, n2, host;
        using (Tenant("T1")) n1 = StorageNameGenerator.Resolve(provider, typeof(NamedDoc), "alpha", Cap);
        using (Tenant("T2")) n2 = StorageNameGenerator.Resolve(provider, typeof(NamedDoc), "alpha", Cap);
        host = StorageNameGenerator.Resolve(provider, typeof(NamedDoc), "alpha", Cap);

        n1.Should().Be("T1-base#alpha");
        n2.Should().Be("T2-base#alpha");   // NOT T1's cached name
        host.Should().Be("base#alpha");
    }

    [Fact]
    public void A_lossy_axis_value_fails_closed()
    {
        RegisterTenantContainerAxis();
        using (Tenant("acme/east"))
            FluentActions.Invoking(() => StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", Cap()))
                .Should().Throw<ArgumentException>().WithMessage("*not identifier-injective*");
        using (Tenant("acme_east"))
            StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", Cap()).Should().Be("acme_east-base#alpha");
    }

    [Fact]
    public void A_non_string_container_value_fails_closed()
    {
        // clrType defaults to string so Validate passes, but a value provider that returns a non-string would collapse
        // via ToString() with no injectivity parity — fail closed at name resolution (ARCH-0101 §8).
        DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("tenant").Mode(AxisMode.Container).AppliesTo(t => t == typeof(NamedDoc)).Field("__t", () => 42),
        }, new ServiceCollection());

        FluentActions.Invoking(() => StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", Cap()))
            .Should().Throw<InvalidOperationException>().WithMessage("*must be a string*");
    }

    [Fact]
    public void On_a_case_folding_adapter_a_mixed_case_value_fails_closed()
    {
        RegisterTenantContainerAxis();
        using (Tenant("Acme"))
            FluentActions.Invoking(() => StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", LowercaseCap()))
                .Should().Throw<ArgumentException>().WithMessage("*not identifier-injective*");
        using (Tenant("acme"))
            StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", LowercaseCap()).Should().Be("acme-base#alpha");
    }
}
