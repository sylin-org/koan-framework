using AwesomeAssertions;
using Koan.Tenancy;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// ARCH-0099 §1 — the boot pre-flight policy, pure and host-free. Refuses to boot only in Production for the
/// exploitable-absence set (no resolver / dev-branded artifact / forced dev-open); never blocks outside
/// Production. The wiring (that a hard failure actually aborts a real boot) is proven in
/// <see cref="TenancyBootPreflightSpec"/>.
/// </summary>
public sealed class TenancyPreflightSpec
{
    private static TenancyPreflightInput Prod(bool resolver = true, bool branded = false, bool forcedOpen = false)
        => new(IsProduction: true, OverrideRequestedOpen: forcedOpen, HasResolver: resolver, BrandedDevMarkerPresent: branded);

    [Fact]
    public void Production_with_a_resolver_and_no_marker_passes()
        => TenancyPreflight.Evaluate(Prod()).ShouldRefuseBoot.Should().BeFalse();

    [Fact]
    public void Production_with_no_resolver_refuses_boot()
    {
        var r = TenancyPreflight.Evaluate(Prod(resolver: false));
        r.ShouldRefuseBoot.Should().BeTrue();
        r.HardFailures.Should().ContainSingle().Which.Should().Contain("no tenant resolver");
    }

    [Fact]
    public void Production_with_a_branded_dev_artifact_refuses_boot()
    {
        var r = TenancyPreflight.Evaluate(Prod(branded: true));
        r.ShouldRefuseBoot.Should().BeTrue();
        r.HardFailures.Should().ContainSingle().Which.Should().Contain(TenancyDevBrand.Prefix);
    }

    [Fact]
    public void Production_with_a_forced_open_posture_refuses_boot()
    {
        var r = TenancyPreflight.Evaluate(Prod(forcedOpen: true));
        r.ShouldRefuseBoot.Should().BeTrue();
        r.HardFailures.Should().ContainSingle().Which.Should().Contain("Dev-open in production");
    }

    [Fact]
    public void Production_accumulates_every_failure()
    {
        var r = TenancyPreflight.Evaluate(new TenancyPreflightInput(
            IsProduction: true, OverrideRequestedOpen: true, HasResolver: false, BrandedDevMarkerPresent: true));
        r.HardFailures.Should().HaveCount(3);
    }

    [Fact]
    public void Non_production_never_refuses_boot_even_with_no_resolver()
    {
        var r = TenancyPreflight.Evaluate(new TenancyPreflightInput(
            IsProduction: false, OverrideRequestedOpen: false, HasResolver: false, BrandedDevMarkerPresent: false));
        r.ShouldRefuseBoot.Should().BeFalse();
        r.Warnings.Should().ContainSingle().Which.Should().Contain("no ITenantResolver");
    }

    [Fact]
    public void Non_production_with_a_resolver_is_silent()
    {
        var r = TenancyPreflight.Evaluate(new TenancyPreflightInput(
            IsProduction: false, OverrideRequestedOpen: false, HasResolver: true, BrandedDevMarkerPresent: false));
        r.ShouldRefuseBoot.Should().BeFalse();
        r.Warnings.Should().BeEmpty();
    }
}
