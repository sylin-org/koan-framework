using System.Collections.Immutable;
using Koan.Core.Diagnostics;
using Koan.Core.Infrastructure;

namespace Koan.Core.Semantics.Contributions;

/// <summary>The immutable contribution result for every target compiled for one host.</summary>
internal sealed class SemanticContributionCompilationSnapshot
{
    public SemanticContributionCompilationSnapshot(
        IEnumerable<SemanticContributionTargetSnapshot> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        Targets = targets.ToImmutableArray();
    }

    public ImmutableArray<SemanticContributionTargetSnapshot> Targets { get; }

    public SemanticContributionTargetSnapshot Get<TTarget>() =>
        Targets.Single(target => target.TargetType == typeof(TTarget));

    internal IReadOnlyList<KoanFact> ToFacts() => Targets
        .SelectMany(target => target.AppliedOwners.Select(owner => KoanFact.Create(
            Constants.Diagnostics.Codes.SemanticContributionApplied,
            KoanFactKind.Capability,
            KoanFactState.Selected,
            $"target:{target.TargetType.Name}/owner:{owner.Value}",
            $"Koan compiled {owner.Value}'s contribution to {target.TargetType.Name} once for this host.",
            Constants.Diagnostics.Reasons.SemanticContributionApplied,
            null,
            owner.Value,
            $"semantic-contribution:{target.TargetType.Name.ToLowerInvariant()}:{owner.Value.ToLowerInvariant()}")))
        .ToArray();
}

/// <summary>One immutable target compilation, preserving constitution decisions and dispatch order.</summary>
internal sealed class SemanticContributionTargetSnapshot
{
    public SemanticContributionTargetSnapshot(
        Type targetType,
        IEnumerable<SemanticId> appliedOwners,
        IEnumerable<SemanticDecision> decisions,
        IEnumerable<SemanticProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(appliedOwners);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(problems);

        TargetType = targetType;
        AppliedOwners = appliedOwners.ToImmutableArray();
        Decisions = decisions.ToImmutableArray();
        Problems = problems.ToImmutableArray();
    }

    public Type TargetType { get; }

    public ImmutableArray<SemanticId> AppliedOwners { get; }

    public ImmutableArray<SemanticDecision> Decisions { get; }

    public ImmutableArray<SemanticProblem> Problems { get; }
}
