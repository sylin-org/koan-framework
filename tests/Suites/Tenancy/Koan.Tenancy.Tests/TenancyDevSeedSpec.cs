using AwesomeAssertions;
using Koan.Tenancy;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// ARCH-0099 §1 — the dev auto-seed composition, pure and host-free: smart-naming (email → SLD label, username
/// → title-case, nothing → the friendly "Acme" not "default"), the Owner role-on-membership, and the
/// per-machine-stable branded signing key. The real-boot wiring is proven in <see cref="TenancyDevAutoSeedSpec"/>.
/// </summary>
public sealed class TenancyDevSeedSpec
{
    [Theory]
    [InlineData("leo@acme.dev", "Acme")]
    [InlineData("jane@bigcorp.io", "Bigcorp")]
    [InlineData("leo", "Leo")]
    [InlineData(null, "Acme")]
    [InlineData("", "Acme")]
    [InlineData("   ", "Acme")]
    public void DeriveTenantName_smart_names_or_defaults_to_Acme(string? input, string expected)
        => TenancyDevSeed.DeriveTenantName(input).Should().Be(expected);

    [Fact]
    public void Create_makes_the_caller_owner_of_a_smart_named_dev_tenant_with_a_branded_key()
    {
        var seed = TenancyDevSeed.Create("leo@acme.dev", "DEV-MACHINE");

        seed.TenantId.Should().Be(TenancyDevSeed.DevTenantId);
        seed.TenantName.Should().Be("Acme");
        seed.OwnerIdentityId.Should().Be("leo@acme.dev");
        seed.OwnerRole.Should().Be(TenancyRoles.Owner);
        seed.OwnerRole.Should().Be("koan:owner");
        seed.SigningKey.Should().StartWith(TenancyDevBrand.Prefix);
    }

    [Fact]
    public void Create_with_no_user_falls_back_to_a_local_owner()
    {
        var seed = TenancyDevSeed.Create(null, "DEV-MACHINE");
        seed.TenantName.Should().Be("Acme");
        seed.OwnerIdentityId.Should().Be("dev@localhost");
    }

    [Fact]
    public void MintKey_is_stable_per_machine_and_user_but_varies_otherwise()
    {
        TenancyDevSeed.MintKey("M", "u").Should().Be(TenancyDevSeed.MintKey("M", "u"));
        TenancyDevSeed.MintKey("M", "u").Should().NotBe(TenancyDevSeed.MintKey("M", "v"));
        TenancyDevSeed.MintKey("M", "u").Should().NotBe(TenancyDevSeed.MintKey("N", "u"));
        TenancyDevSeed.MintKey("M", "u").Should().StartWith(TenancyDevBrand.Prefix);
    }
}
