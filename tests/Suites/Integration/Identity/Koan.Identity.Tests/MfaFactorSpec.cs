using System.Security.Claims;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Credentials;
using Koan.Identity.Credentials.Checkup;
using Koan.Identity.Mfa;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0007 P3-grp4 Phase 1 (ARCH-0079 — real <c>AddKoan()</c>, offline): the MFA factor — TOTP enrollment with the
/// secret encrypted at rest, anti-replay, single-use recovery codes, the step-up interruption driven by the REAL MFA
/// requirement (through the live dispatcher), and the Security Checkup amber → green journey (the delight).
/// </summary>
[Collection("identity")]
public sealed class MfaFactorSpec : IdentityHostScopedSpec
{
    private readonly IdentityHostFixture _fx;
    public MfaFactorSpec(IdentityHostFixture fx) : base(fx) => _fx = fx;

    private TotpService Totp => _fx.Services.GetRequiredService<TotpService>();
    private RecoveryCodeService Recovery => _fx.Services.GetRequiredService<RecoveryCodeService>();
    private static string CodeFor(string secretBase32) => new Totp(Base32Encoding.ToBytes(secretBase32)).ComputeTotp();

    private static async Task<AuthSignInContext> DispatchSignInAsync(IServiceScope scope, string personId, params string[] amr)
    {
        var ci = new ClaimsIdentity("test");
        ci.AddClaim(new Claim(ClaimTypes.NameIdentifier, personId));
        CredentialAuthClaims.Stamp(ci, amr);
        var ctx = new AuthSignInContext
        {
            Provider = "local",
            Identity = ci,
            Services = scope.ServiceProvider,
            HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider },
        };
        await scope.ServiceProvider.GetRequiredService<AuthFlowDispatcher>().DispatchSignIn(ctx, default);
        return ctx;
    }

    [Fact]
    public async Task Enroll_then_confirm_activates_totp_and_replays_are_rejected()
    {
        await new Identity { Id = "mfa-enroll", DisplayName = "Enroll" }.Save();
        var enrollment = await Totp.BeginEnrollmentAsync("mfa-enroll", "enroll@corp.com", "Koan");
        enrollment.OtpAuthUri.Should().StartWith("otpauth://totp/", "the QR URI is returned for the authenticator");
        (await Totp.IsConfirmedAsync("mfa-enroll")).Should().BeFalse("an enrollment is inactive until confirmed");

        (await Totp.ConfirmAsync("mfa-enroll", "000000")).Should().BeFalse("a wrong code does not confirm");
        var code = CodeFor(enrollment.SecretBase32);
        (await Totp.ConfirmAsync("mfa-enroll", code)).Should().BeTrue("a valid code confirms enrollment");
        (await Totp.IsConfirmedAsync("mfa-enroll")).Should().BeTrue();
        (await Totp.ConfirmAsync("mfa-enroll", code)).Should().BeFalse("the same code cannot be replayed (anti-replay)");
    }

    [Fact]
    public async Task The_totp_secret_is_encrypted_at_rest()
    {
        await new Identity { Id = "mfa-enc", DisplayName = "Enc" }.Save();
        var enrollment = await Totp.BeginEnrollmentAsync("mfa-enc", "enc@corp.com", "Koan");

        var stored = (await MfaEnrollment.Get(MfaEnrollment.KeyFor("mfa-enc", MfaType.Totp)))!;
        stored.Secret.Should().NotBe(enrollment.SecretBase32, "the secret is stored protected, not in plaintext");
        var protector = _fx.Services.GetRequiredService<IMfaSecretProtector>();
        protector.Unprotect(stored.Secret).Should().Be(enrollment.SecretBase32, "and round-trips back via the protector");
    }

    [Fact]
    public async Task A_confirmed_factor_BLOCKS_a_password_only_sign_in_and_a_second_factor_completes_it()
    {
        await new Identity { Id = "mfa-gate", DisplayName = "Gate" }.Save();
        var enrollment = await Totp.BeginEnrollmentAsync("mfa-gate", "gate@corp.com", "Koan");
        (await Totp.ConfirmAsync("mfa-gate", CodeFor(enrollment.SecretBase32))).Should().BeTrue();

        using var scope = _fx.Services.CreateScope();
        // Password alone is now interrupted (the REAL MFA requirement drives the gate)...
        var blocked = await DispatchSignInAsync(scope, "mfa-gate", CredentialAuthClaims.Password);
        blocked.RejectReason.Should().NotBeNull("a confirmed MFA factor requires a second factor at sign-in");
        (await Session.Query(s => s.IdentityId == "mfa-gate")).Should().BeEmpty("no session is issued by password alone");

        // ...the resumed sign-in (the challenge proved otp) completes.
        var completed = await DispatchSignInAsync(scope, "mfa-gate", CredentialAuthClaims.Password, CredentialAuthClaims.Totp);
        completed.RejectReason.Should().BeNull("pwd + otp satisfies the requirement");
        (await Session.Query(s => s.IdentityId == "mfa-gate")).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Recovery_codes_are_single_use_and_a_regeneration_replaces_the_set()
    {
        await new Identity { Id = "mfa-rec", DisplayName = "Rec" }.Save();
        var codes = await Recovery.GenerateAsync("mfa-rec", count: 8);
        codes.Should().HaveCount(8).And.OnlyHaveUniqueItems();
        (await Recovery.RemainingAsync("mfa-rec")).Should().Be(8);

        (await Recovery.RedeemAsync("mfa-rec", codes[0])).Should().BeTrue("a fresh code redeems");
        (await Recovery.RedeemAsync("mfa-rec", codes[0])).Should().BeFalse("the same code is single-use");
        (await Recovery.RedeemAsync("mfa-rec", "zzzzz-zzzzz")).Should().BeFalse("an unknown code fails closed");
        (await Recovery.RemainingAsync("mfa-rec")).Should().Be(7);

        var fresh = await Recovery.GenerateAsync("mfa-rec");
        (await Recovery.RedeemAsync("mfa-rec", codes[1])).Should().BeFalse("regenerating invalidates the prior set");
        (await Recovery.RedeemAsync("mfa-rec", fresh[0])).Should().BeTrue("the new set works");
    }

    [Fact]
    public async Task The_security_checkup_walks_amber_to_green_as_factors_are_added()
    {
        await new Identity { Id = "mfa-checkup", DisplayName = "Checkup" }.Save();
        // Koan owns this person's primary factor (a local password), so the MFA nudge is honest for them.
        await _fx.Services.GetRequiredService<Koan.Identity.Passwords.PasswordCredentialService>()
            .SetPasswordAsync("mfa-checkup", "checkup@corp.com", "pw");
        SecurityCheckupResolver Checkup() => _fx.Services.CreateScope().ServiceProvider.GetRequiredService<SecurityCheckupResolver>();

        // 1) No second factor → amber, with the "Add 2FA" nudge.
        var step1 = await Checkup().EvaluateAsync("mfa-checkup");
        step1.Overall.Should().Be(CheckupGrade.Amber);
        step1.Signals.Should().Contain(s => s.Category == "mfa" && s.Grade == CheckupGrade.Amber && s.Action == "Add 2FA");

        // 2) MFA on, but no recovery yet → the recovery nudge leads (pre-provisioned-recovery-as-care).
        var enrollment = await Totp.BeginEnrollmentAsync("mfa-checkup", "checkup@corp.com", "Koan");
        (await Totp.ConfirmAsync("mfa-checkup", CodeFor(enrollment.SecretBase32))).Should().BeTrue();
        var step2 = await Checkup().EvaluateAsync("mfa-checkup");
        step2.Signals.Should().Contain(s => s.Category == "mfa" && s.Grade == CheckupGrade.Green);
        step2.Signals.Should().Contain(s => s.Category == "recovery" && s.Grade == CheckupGrade.Amber);
        step2.Overall.Should().Be(CheckupGrade.Amber);

        // 3) Recovery provisioned → all green (the calm landing).
        await Recovery.GenerateAsync("mfa-checkup");
        var step3 = await Checkup().EvaluateAsync("mfa-checkup");
        step3.Overall.Should().Be(CheckupGrade.Green, "MFA on + recovery set → a calm, all-green Security Checkup");
    }

    [Fact]
    public async Task A_redeemed_recovery_code_satisfies_the_step_up_gate()
    {
        await new Identity { Id = "mfa-recgate", DisplayName = "RecGate" }.Save();
        var e = await Totp.BeginEnrollmentAsync("mfa-recgate", "recgate@corp.com", "Koan");
        (await Totp.ConfirmAsync("mfa-recgate", CodeFor(e.SecretBase32))).Should().BeTrue();

        using var scope = _fx.Services.CreateScope();
        // pwd + recovery (a redeemed recovery code stamps amr=recovery) completes the gate — the lockout safety net is honest.
        var ctx = await DispatchSignInAsync(scope, "mfa-recgate", CredentialAuthClaims.Password, CredentialAuthClaims.Recovery);
        ctx.RejectReason.Should().BeNull("a redeemed recovery code satisfies the MFA requirement (no authenticator-loss lockout)");
        (await Session.Query(s => s.IdentityId == "mfa-recgate")).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Too_many_wrong_codes_locks_the_factor_against_brute_force()
    {
        await new Identity { Id = "mfa-lock", DisplayName = "Lock" }.Save();
        var e = await Totp.BeginEnrollmentAsync("mfa-lock", "lock@corp.com", "Koan");
        (await Totp.ConfirmAsync("mfa-lock", CodeFor(e.SecretBase32))).Should().BeTrue();

        for (var i = 0; i < 5; i++)
            (await Totp.VerifyAsync("mfa-lock", "000000")).Should().BeFalse("a wrong code is rejected");

        var enrollment = (await MfaEnrollment.Get(MfaEnrollment.KeyFor("mfa-lock", MfaType.Totp)))!;
        enrollment.IsLockedAt(DateTimeOffset.UtcNow).Should().BeTrue("repeated wrong codes trigger a brute-force lockout (NIST 800-63B)");
    }
}
