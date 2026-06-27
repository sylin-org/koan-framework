using Koan.Core;

namespace Koan.Identity.Credentials.StepUp;

/// <summary>
/// A step-up factor a person must prove to complete sign-in. <see cref="Factor"/> is the requirement class (e.g.
/// <c>mfa</c>); <see cref="SatisfiedBy"/> is the set of <c>amr</c> methods that fulfil it (e.g. <c>otp</c>, <c>passkey</c>).
/// </summary>
public sealed record StepUpRequirement(string Factor, IReadOnlySet<string> SatisfiedBy);

/// <summary>
/// SEC-0007 P3-grp4 — a discovered contributor declaring the step-up factors a person must prove at sign-in. The Mfa
/// package contributes <c>mfa</c> when the person has a confirmed authenticator. The contributor-pipeline canon: the
/// generic step-up gate composes whatever factors are registered — no bespoke per-factor branching, graceful
/// degradation (no factor packages ⇒ no requirements ⇒ single-factor sign-in unchanged).
/// </summary>
[KoanDiscoverable]
public interface IStepUpRequirementContributor
{
    /// <summary>The step-up requirements that apply to <paramref name="identityId"/> right now.</summary>
    Task<IReadOnlyList<StepUpRequirement>> RequiredForAsync(string identityId, CancellationToken ct = default);
}
