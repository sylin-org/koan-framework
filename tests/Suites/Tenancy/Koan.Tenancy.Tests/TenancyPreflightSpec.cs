using AwesomeAssertions;
using Koan.Tenancy;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// ARCH-0099 §1 — the boot pre-flight policy, pure and host-free. The load-bearing invariant: a RESOLVED Open
/// posture is legal only in Development (so a forced-Open in Staging/Prod refuses the boot regardless of how the
/// override arrived); Production additionally requires a resolver; a dev-branded artifact outside dev refuses;
/// outside Production a missing resolver only warns. The real-boot wiring is proven in
/// <see cref="TenancyBootPreflightSpec"/>.
/// </summary>
public sealed class TenancyPreflightSpec
{
    private static TenancyPreflightResult Evaluate(TenancyPreflightInput i) => TenancyPreflight.Evaluate(i);

    private static TenancyPreflightInput Dev(bool open = true, bool resolver = false, bool branded = false)
        => new(IsDevelopment: true, IsProduction: false, PostureIsOpen: open, HasResolver: resolver, BrandedDevMarkerPresent: branded);
    private static TenancyPreflightInput Prod(bool open = false, bool resolver = true, bool branded = false)
        => new(IsDevelopment: false, IsProduction: true, PostureIsOpen: open, HasResolver: resolver, BrandedDevMarkerPresent: branded);
    private static TenancyPreflightInput Staging(bool open = false, bool resolver = false, bool branded = false)
        => new(IsDevelopment: false, IsProduction: false, PostureIsOpen: open, HasResolver: resolver, BrandedDevMarkerPresent: branded);

    [Fact]
    public void Development_open_with_no_resolver_and_a_branded_key_passes()
        => Evaluate(Dev(open: true, resolver: false, branded: true)).ShouldRefuseBoot.Should().BeFalse();

    [Fact]
    public void Production_closed_with_a_resolver_passes()
        => Evaluate(Prod()).ShouldRefuseBoot.Should().BeFalse();

    [Fact]
    public void Open_posture_in_production_refuses_boot()
    {
        var r = Evaluate(Prod(open: true));
        r.ShouldRefuseBoot.Should().BeTrue();
        r.HardFailures.Should().Contain(f => f.Contains("legal only in Development"));
    }

    [Fact]
    public void Open_posture_in_staging_refuses_boot()
    {
        // The KEY fix: a forced-Open in any non-Development environment (not just Production) is refused.
        var r = Evaluate(Staging(open: true));
        r.ShouldRefuseBoot.Should().BeTrue();
        r.HardFailures.Should().Contain(f => f.Contains("legal only in Development"));
    }

    [Fact]
    public void Production_with_no_resolver_refuses_boot()
        => Evaluate(Prod(resolver: false)).HardFailures.Should().Contain(f => f.Contains("no tenant resolver"));

    [Fact]
    public void Branded_artifact_outside_development_refuses_boot()
        => Evaluate(Staging(branded: true)).HardFailures.Should().Contain(f => f.Contains(TenancyDevBrand.Prefix));

    [Fact]
    public void Production_accumulates_every_failure()
        => Evaluate(Prod(open: true, resolver: false, branded: true)).HardFailures.Should().HaveCountGreaterThanOrEqualTo(3);

    [Fact]
    public void Staging_closed_with_no_resolver_warns_but_does_not_block()
    {
        var r = Evaluate(Staging(resolver: false));
        r.ShouldRefuseBoot.Should().BeFalse();
        r.Warnings.Should().ContainSingle().Which.Should().Contain("no ITenantResolver");
    }

    [Fact]
    public void Staging_closed_with_a_resolver_is_silent()
    {
        var r = Evaluate(Staging(resolver: true));
        r.ShouldRefuseBoot.Should().BeFalse();
        r.Warnings.Should().BeEmpty();
    }
}
