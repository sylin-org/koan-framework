using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Credentials;
using Koan.Identity.Passwords;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0007 P3-grp4 Phase 1 (ARCH-0079 — real <c>AddKoan()</c>, offline): the local password factor — a portable
/// BCrypt hash (D2), fail-closed verify, and upgrade-on-verify rehash.
/// </summary>
[Collection("identity")]
public sealed class PasswordFactorSpec
{
    private readonly IdentityHostFixture _fx;
    public PasswordFactorSpec(IdentityHostFixture fx) => _fx = fx;

    private PasswordCredentialService Passwords => _fx.Services.GetRequiredService<PasswordCredentialService>();

    [Fact]
    public async Task Set_then_verify_returns_the_person_and_stores_a_portable_hash()
    {
        await new Identity { Id = "pw-alice", DisplayName = "Alice" }.Save();
        var cred = await Passwords.SetPasswordAsync("pw-alice", "Alice@corp.com", "correct horse battery staple");

        cred.PasswordHash.Should().StartWith("$2", "the stored hash is a portable bcrypt MCF string (D2)");
        cred.IdentityId.Should().Be("pw-alice");
        cred.LoginIdentifier.Should().Be("alice@corp.com", "the login handle is normalized");

        (await Passwords.VerifyAsync("alice@corp.com", "correct horse battery staple")).Should().Be("pw-alice");
        (await Passwords.VerifyAsync("Alice@Corp.com", "correct horse battery staple")).Should().Be("pw-alice", "login is case-insensitive via normalization");
        (await Passwords.VerifyAsync("alice@corp.com", "wrong")).Should().BeNull("a wrong password never authenticates");
        (await Passwords.VerifyAsync("nobody@corp.com", "whatever")).Should().BeNull("an unknown login fails closed");
    }

    [Fact]
    public async Task Verify_upgrades_a_below_policy_hash_in_place()
    {
        await new Identity { Id = "pw-upgrade", DisplayName = "Up" }.Save();
        // Store at a LOW cost, then verify with a current-policy (higher cost) service — the one moment we hold the plaintext.
        var weak = new PasswordCredentialService(new BcryptPasswordHasher(workFactor: 6));
        var strong = new PasswordCredentialService(new BcryptPasswordHasher(workFactor: 12));
        var stored = await weak.SetPasswordAsync("pw-upgrade", "up@corp.com", "s3cret");
        var before = stored.PasswordHash;

        (await strong.VerifyAsync("up@corp.com", "s3cret")).Should().Be("pw-upgrade");

        var after = (await LocalCredential.Get(LocalCredential.KeyFor("pw-upgrade")))!.PasswordHash;
        after.Should().NotBe(before, "upgrade-on-verify rehashed at the current policy cost");
        new BcryptPasswordHasher(12).Verify("s3cret", after).Should().BeTrue("the rehash still verifies");
    }

    [Fact]
    public void A_malformed_stored_hash_never_authenticates()
        => new BcryptPasswordHasher().Verify("anything", "not-a-bcrypt-hash").Should().BeFalse();

    [Fact]
    public async Task A_login_identifier_already_owned_by_another_person_is_rejected()
    {
        await new Identity { Id = "pw-owner", DisplayName = "Owner" }.Save();
        await new Identity { Id = "pw-intruder", DisplayName = "Intruder" }.Save();
        await Passwords.SetPasswordAsync("pw-owner", "shared@corp.com", "owner-pw");

        var act = async () => await Passwords.SetPasswordAsync("pw-intruder", "shared@corp.com", "intruder-pw");
        await act.Should().ThrowAsync<InvalidOperationException>("a login handle must map to ONE person — else both are silently locked out");

        (await Passwords.VerifyAsync("shared@corp.com", "owner-pw")).Should().Be("pw-owner", "the original owner's login is unaffected");
    }

    [Fact]
    public async Task HasPassword_reflects_whether_a_local_password_is_set()
    {
        await new Identity { Id = "pw-has", DisplayName = "Has" }.Save();
        (await Passwords.HasPasswordAsync("pw-has")).Should().BeFalse();
        await Passwords.SetPasswordAsync("pw-has", "has@corp.com", "pw");
        (await Passwords.HasPasswordAsync("pw-has")).Should().BeTrue();
    }
}
