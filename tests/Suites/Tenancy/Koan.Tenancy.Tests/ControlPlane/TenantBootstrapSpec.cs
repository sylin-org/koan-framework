using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Tenancy;
using Koan.Tenancy.Tests.Support;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// ARCH-0099 §2 — first-owner onboarding. The pure <see cref="TenantBootstrapPolicy"/> (dev allows anyone;
/// prod gates on allowlist OR a constant-time-compared one-time token) and the durable one-shot
/// <see cref="TenantBootstrap"/> claim (proven through a real <c>AddKoan()</c> boot, ARCH-0079): the first
/// claimant becomes the only Owner, a second claim is ignored, and the dev graduation is idempotent.
/// </summary>
public sealed class TenantBootstrapSpec
{
    private static bool CanClaim(bool dev, string? id, string[]? allow, string? token, string? expected)
        => TenantBootstrapPolicy.CanClaim(dev, id, allow, token, expected);

    private static IDisposable Iso() => EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

    [Fact] public void Development_allows_anyone_to_claim() => CanClaim(true, null, null, null, null).Should().BeTrue();

    [Fact] public void Production_allows_an_allowlisted_identity()
        => CanClaim(false, "leo@acme.dev", new[] { "LEO@acme.dev" }, null, null).Should().BeTrue(); // case-insensitive

    [Fact] public void Production_allows_a_matching_one_time_token()
        => CanClaim(false, "anyone", null, "s3cret-token", "s3cret-token").Should().BeTrue();

    [Fact] public void Production_denies_a_non_allowlisted_identity_with_no_token()
        => CanClaim(false, "evil@x.com", new[] { "leo@acme.dev" }, null, "s3cret-token").Should().BeFalse();

    [Fact] public void Production_denies_a_wrong_token()
        => CanClaim(false, "anyone", null, "wrong", "s3cret-token").Should().BeFalse();

    [Fact]
    public async Task ClaimOwner_makes_the_first_user_the_only_owner_and_ignores_later_claims()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync();
        runtime.ResetEntityCaches();
        using var _ = Iso();

        await TenantBootstrap.EnsureTenantAsync("acme", "Acme");
        (await TenantBootstrap.IsOwnerClaimedAsync("acme")).Should().BeFalse();

        var owner = await TenantBootstrap.ClaimOwnerAsync("acme", "leo@acme.dev");
        owner.IsOwner.Should().BeTrue();
        owner.IdentityId.Should().Be("leo@acme.dev");
        (await TenantBootstrap.IsOwnerClaimedAsync("acme")).Should().BeTrue();

        // A second claim by a different identity is ignored — one-shot. Returns the original owner; no second owner.
        var again = await TenantBootstrap.ClaimOwnerAsync("acme", "intruder@x.com");
        again.Id.Should().Be(owner.Id);
        again.IdentityId.Should().Be("leo@acme.dev");
        (await Membership.Query(m => m.TenantId == "acme")).Count(m => m.IsOwner).Should().Be(1);
    }

    [Fact]
    public async Task EnsureDev_graduates_the_seed_to_durable_rows_idempotently()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync();
        runtime.ResetEntityCaches();
        using var _ = Iso();

        var (t1, o1) = await TenantBootstrap.EnsureDevAsync("dev", "Acme", "leo@acme.dev");
        var (t2, o2) = await TenantBootstrap.EnsureDevAsync("dev", "Acme", "leo@acme.dev");

        t1.Id.Should().Be("dev");
        t2.Id.Should().Be(t1.Id);
        o2.Id.Should().Be(o1.Id);
        o1.IsOwner.Should().BeTrue();
        (await TenantRecord.All()).Count(t => t.Id == "dev").Should().Be(1);
        (await Membership.Query(m => m.TenantId == "dev")).Count(m => m.IsOwner).Should().Be(1);
    }
}
