using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Credentials.Checkup;
using Koan.Identity.Passwords;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0007 P3-grp4 Phase 1 (ARCH-0079 — real <c>AddKoan()</c>, offline): the Security Checkup contributor read-model
/// — the traffic-light posture synthesis shipped as substrate (the lead differentiator). A single Amber dominates the
/// overall light; an optional factor (local password) is never nagged when absent. Commit 2 adds the MFA/recovery
/// amber nudges that make the amber → green journey real.
/// </summary>
[Collection("identity")]
public sealed class SecurityCheckupSpec
{
    private readonly IdentityHostFixture _fx;
    public SecurityCheckupSpec(IdentityHostFixture fx) => _fx = fx;

    private const string AmberPrefix = "ck-amber-";

    private static SecurityCheckupResolver Checkup(IServiceScope s) => s.ServiceProvider.GetRequiredService<SecurityCheckupResolver>();
    private PasswordCredentialService Passwords => _fx.Services.GetRequiredService<PasswordCredentialService>();

    [Fact]
    public async Task Checkup_aggregates_signals_across_contributors()
    {
        await new Identity { Id = "ck-user", DisplayName = "Ck" }.Save();
        await new Session { IdentityId = "ck-user" }.Save();
        await Passwords.SetPasswordAsync("ck-user", "ck@corp.com", "pw");

        using var scope = _fx.Services.CreateScope();
        var checkup = await Checkup(scope).EvaluateAsync("ck-user");

        // The pipeline flattens every referenced factor's signal over one read-model (password + devices + the MFA
        // nudge). Overall is Amber here because there is no second factor yet — MfaFactorSpec proves the amber→green journey.
        checkup.Signals.Should().Contain(s => s.Category == "password" && s.Grade == CheckupGrade.Green, "password set → green");
        checkup.Signals.Should().Contain(s => s.Category == "devices", "the device list is always present (over Session)");
        checkup.Signals.Should().Contain(s => s.Category == "mfa" && s.Grade == CheckupGrade.Amber, "no second factor → the delight nudge");
        checkup.Overall.Should().Be(CheckupGrade.Amber, "the worst signal (the MFA nudge) sets the overall light");
    }

    [Fact]
    public async Task A_passwordless_person_is_not_nagged_for_a_password()
    {
        await new Identity { Id = "ck-passwordless", DisplayName = "PL" }.Save();
        using var scope = _fx.Services.CreateScope();
        var checkup = await Checkup(scope).EvaluateAsync("ck-passwordless");
        checkup.Signals.Should().NotContain(s => s.Category == "password", "local password is optional (D4) — silent when absent");
    }

    [Fact]
    public async Task A_single_amber_signal_dominates_the_overall_light()
    {
        var id = AmberPrefix + "x";
        await new Identity { Id = id, DisplayName = "Amber" }.Save();
        using var scope = _fx.Services.CreateScope();
        var checkup = await Checkup(scope).EvaluateAsync(id);

        checkup.Signals.Should().Contain(s => s.Grade == CheckupGrade.Amber);
        checkup.Overall.Should().Be(CheckupGrade.Amber, "the worst signal sets the overall traffic-light");
    }

    // Discovered, prefix-gated: contributes an amber signal only for "ck-amber-*" persons — inert for everyone else.
    private sealed class StubAmberCheckup : ISecurityCheckupContributor
    {
        public Task<IReadOnlyList<CheckupSignal>> EvaluateAsync(string identityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CheckupSignal>>(
                identityId.StartsWith(AmberPrefix, StringComparison.Ordinal)
                    ? new[] { new CheckupSignal("stub", CheckupGrade.Amber, "stub amber") }
                    : Array.Empty<CheckupSignal>());
    }
}
