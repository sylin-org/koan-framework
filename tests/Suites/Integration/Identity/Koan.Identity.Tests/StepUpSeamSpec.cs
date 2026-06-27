using System.Security.Claims;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Credentials;
using Koan.Identity.Credentials.StepUp;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0007 P3-grp4 Phase 1 (ARCH-0079 — real <c>AddKoan()</c>, offline): the framework-native 2-phase
/// (interrupted → resume) sign-in. The gate is driven through the REAL <see cref="AuthFlowDispatcher"/>, so the
/// enforcement is request-path-real: an unsatisfied required factor aborts the sign-in and creates NO session, and
/// proving the factor on the resumed sign-in completes it. A prefix-gated stub requirement exercises the seam without
/// the Mfa package and stays inert for every other sign-in in the suite.
/// </summary>
[Collection("identity")]
public sealed class StepUpSeamSpec
{
    private readonly IdentityHostFixture _fx;
    public StepUpSeamSpec(IdentityHostFixture fx) => _fx = fx;

    private const string NeedsMfaPrefix = "stepup-needs-mfa-";

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
    public async Task A_person_with_no_requirements_signs_in_and_a_session_is_created()
    {
        await new Identity { Id = "su-clear", DisplayName = "Clear" }.Save();
        using var scope = _fx.Services.CreateScope();
        var ctx = await DispatchSignInAsync(scope, "su-clear", CredentialAuthClaims.Password);

        ctx.RejectReason.Should().BeNull("no step-up requirements → sign-in completes");
        (await Session.Query(s => s.IdentityId == "su-clear")).Should().NotBeEmpty("a session is issued");
        ctx.Identity.HasClaim(CredentialAuthClaims.Amr, CredentialAuthClaims.Password).Should().BeTrue();
        ctx.Identity.HasClaim(CredentialAuthClaims.Acr, CredentialAuthClaims.Aal1).Should().BeTrue("single factor → aal1");
    }

    [Fact]
    public async Task A_required_factor_BLOCKS_the_sign_in_and_creates_NO_session()
    {
        var id = NeedsMfaPrefix + "blocked";
        await new Identity { Id = id, DisplayName = "Blocked" }.Save();
        using var scope = _fx.Services.CreateScope();
        var ctx = await DispatchSignInAsync(scope, id, CredentialAuthClaims.Password);

        ctx.RejectReason.Should().StartWith(StepUpOptions.StepUpRejectPrefix,
            "an unsatisfied step-up requirement aborts the sign-in with a resume ticket (the 2-phase enforcement)");
        (await Session.Query(s => s.IdentityId == id)).Should().BeEmpty("NO session until the second factor is proven — request-path-real, not write-only");
    }

    [Fact]
    public async Task Proving_the_second_factor_completes_the_sign_in()
    {
        var id = NeedsMfaPrefix + "resume";
        await new Identity { Id = id, DisplayName = "Resume" }.Save();
        using var scope = _fx.Services.CreateScope();

        (await DispatchSignInAsync(scope, id, CredentialAuthClaims.Password)).RejectReason.Should().NotBeNull("pwd alone is blocked");

        var ctx = await DispatchSignInAsync(scope, id, CredentialAuthClaims.Password, CredentialAuthClaims.Totp);
        ctx.RejectReason.Should().BeNull("pwd + otp satisfies the requirement");
        ctx.Identity.HasClaim(CredentialAuthClaims.Acr, CredentialAuthClaims.Aal2).Should().BeTrue("two factors → aal2");
        (await Session.Query(s => s.IdentityId == id)).Should().NotBeEmpty("the resumed sign-in issues a session");
    }

    [Fact]
    public async Task A_step_up_ticket_is_single_use()
    {
        using var scope = _fx.Services.CreateScope();
        var stepUp = scope.ServiceProvider.GetRequiredService<StepUpService>();
        var ticket = await stepUp.IssueTicketAsync("su-ticket", new HashSet<string>(StringComparer.Ordinal) { "pwd" }, new[] { "mfa" });

        (await stepUp.ResolveTicketAsync(ticket.Id)).Should().NotBeNull("a fresh ticket resolves");
        (await stepUp.ConsumeAsync(ticket)).Should().BeTrue("first consume wins");
        (await stepUp.ResolveTicketAsync(ticket.Id)).Should().BeNull("a consumed ticket no longer resolves");
        (await stepUp.ConsumeAsync(ticket)).Should().BeFalse("a replayed consume is refused (single-use)");
    }

    [Fact]
    public async Task A_gate_that_throws_fails_CLOSED_and_issues_no_session()
    {
        await new Identity { Id = "throwgate-x", DisplayName = "Throw" }.Save();
        using var scope = _fx.Services.CreateScope();
        var ctx = await DispatchSignInAsync(scope, "throwgate-x", CredentialAuthClaims.Password);

        ctx.RejectReason.Should().NotBeNull("a gate that cannot be evaluated must ABORT the sign-in — never fall through to an allowed session");
        (await Session.Query(s => s.IdentityId == "throwgate-x")).Should().BeEmpty("no session is issued when a sign-in gate faults (fail closed)");
    }

    // Discovered, prefix-gated: requires "mfa" only for "stepup-needs-mfa-*" persons — inert for every other sign-in.
    private sealed class StubMfaRequirement : IStepUpRequirementContributor
    {
        public Task<IReadOnlyList<StepUpRequirement>> RequiredForAsync(string identityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StepUpRequirement>>(
                identityId.StartsWith(NeedsMfaPrefix, StringComparison.Ordinal)
                    ? new[] { new StepUpRequirement("mfa", new HashSet<string>(StringComparer.Ordinal) { CredentialAuthClaims.Totp, CredentialAuthClaims.Passkey }) }
                    : Array.Empty<StepUpRequirement>());
    }

    // Discovered, prefix-gated: throws only for "throwgate-*" persons — proves the gate loop fails CLOSED on a fault.
    private sealed class ThrowingGate : ISignInGate
    {
        public Task<SignInGateBlock?> EvaluateAsync(string identityId, ClaimsPrincipal principal, IServiceProvider services, CancellationToken ct = default)
            => identityId.StartsWith("throwgate-", StringComparison.Ordinal)
                ? throw new InvalidOperationException("simulated sign-in gate fault")
                : Task.FromResult<SignInGateBlock?>(null);
    }
}
