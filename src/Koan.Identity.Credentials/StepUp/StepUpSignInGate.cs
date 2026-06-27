using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Identity.Credentials.StepUp;

/// <summary>
/// SEC-0007 P3-grp4 — the generic 2-phase enforcement (the <see cref="ISignInGate"/> the sign-in pipeline consults
/// before issuing a session). If the person has step-up requirements not yet satisfied by the factors proven this
/// round (the principal's <c>amr</c>), it mints a resume ticket and BLOCKS — the cookie is emptied, no
/// <c>Session</c> is created, so a required-but-unproven factor cannot yield a full session. Generic over whatever
/// <see cref="IStepUpRequirementContributor"/>s are registered; allows when all are satisfied (or none exist).
/// </summary>
internal sealed class StepUpSignInGate : ISignInGate
{
    public int Order => 0;

    public async Task<SignInGateBlock?> EvaluateAsync(string identityId, ClaimsPrincipal principal, IServiceProvider services, CancellationToken ct = default)
    {
        var stepUp = services.GetService<StepUpService>();
        if (stepUp is null) return null; // base not wired — allow

        var proven = CredentialAuthClaims.MethodsOf(principal);
        var unsatisfied = await stepUp.UnsatisfiedAsync(identityId, proven, ct).ConfigureAwait(false);
        if (unsatisfied.Count == 0) return null; // all requirements met (or none) — allow

        var ticket = await stepUp.IssueTicketAsync(identityId, proven, unsatisfied.Select(r => r.Factor), ct).ConfigureAwait(false);
        return new SignInGateBlock(StepUpOptions.StepUpRejectPrefix + ticket.Id);
    }
}
