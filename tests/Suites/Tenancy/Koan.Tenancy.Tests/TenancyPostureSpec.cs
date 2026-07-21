using AwesomeAssertions;
using Koan.Tenancy;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// ARCH-0099 §1 — the posture derivation. Pure and host-free: the ASP.NET rule verbatim (positive
/// <c>IsDevelopment()</c> ⇒ Open; everything else ⇒ Closed), an explicit override wins, and the default enum
/// value is fail-safe (Closed). The gate's <i>behavior</i> under each posture is proven through a real boot in
/// <see cref="TenantEnforcementSpec"/>.
/// </summary>
public sealed class TenancyPostureSpec
{
    [Fact]
    public void Development_resolves_to_Open()
        => TenancyPostureResolver.Resolve(isDevelopment: true).Should().Be(TenancyPosture.Open);

    [Fact]
    public void Non_development_resolves_to_Closed()
        => TenancyPostureResolver.Resolve(isDevelopment: false).Should().Be(TenancyPosture.Closed);

    [Fact]
    public void An_explicit_override_wins_over_the_environment()
    {
        TenancyPostureResolver.Resolve(isDevelopment: true, TenancyPosture.Closed).Should().Be(TenancyPosture.Closed);
        TenancyPostureResolver.Resolve(isDevelopment: false, TenancyPosture.Open).Should().Be(TenancyPosture.Open);
    }

    [Fact]
    public void Default_posture_value_is_Closed_fail_safe()
        => default(TenancyPosture).Should().Be(TenancyPosture.Closed);
}
