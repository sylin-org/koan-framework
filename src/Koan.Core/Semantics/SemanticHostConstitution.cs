using System.Collections.Immutable;
using Koan.Core.Diagnostics;
using Koan.Core.Infrastructure;

namespace Koan.Core.Semantics;

/// <summary>
/// The immutable, instance-free result of compiling one application's declared Koan capabilities.
/// </summary>
internal sealed class SemanticHostConstitution
{
    internal SemanticHostConstitution(
        IEnumerable<SemanticComponentDescriptor> activeDescriptors,
        IEnumerable<SemanticComponentDescriptor> inactiveDescriptors,
        IEnumerable<SemanticDecision> decisions,
        IEnumerable<SemanticProblem> problems,
        bool isDegraded)
    {
        ActiveDescriptors = activeDescriptors.ToImmutableArray();
        InactiveDescriptors = inactiveDescriptors.ToImmutableArray();
        Decisions = decisions.ToImmutableArray();
        Problems = problems.ToImmutableArray();
        IsDegraded = isDegraded;
        ActiveIds = ActiveDescriptors.Select(static descriptor => descriptor.Id).ToImmutableArray();
        InactiveIds = InactiveDescriptors.Select(static descriptor => descriptor.Id).ToImmutableArray();
    }

    public ImmutableArray<SemanticId> ActiveIds { get; }

    public ImmutableArray<SemanticId> InactiveIds { get; }

    public ImmutableArray<SemanticDecision> Decisions { get; }

    public ImmutableArray<SemanticProblem> Problems { get; }

    public bool IsDegraded { get; }

    internal ImmutableArray<SemanticComponentDescriptor> ActiveDescriptors { get; }

    internal ImmutableArray<SemanticComponentDescriptor> InactiveDescriptors { get; }

    internal IReadOnlyList<KoanFact> ToFacts()
    {
        var corrections = Problems.ToLookup(static problem => problem.Owner);
        return Decisions.Select(decision =>
        {
            var problem = corrections[decision.Component].FirstOrDefault();
            var code = decision.State switch
            {
                SemanticDecisionState.Active => Constants.Diagnostics.Codes.SemanticComponentActive,
                SemanticDecisionState.Inactive => Constants.Diagnostics.Codes.SemanticComponentInactive,
                _ => Constants.Diagnostics.Codes.SemanticComponentRejected,
            };
            var kind = decision.State == SemanticDecisionState.Rejected
                ? KoanFactKind.Rejection
                : KoanFactKind.Capability;
            var state = decision.State switch
            {
                SemanticDecisionState.Active when IsDegraded => KoanFactState.Degraded,
                SemanticDecisionState.Active => KoanFactState.Selected,
                SemanticDecisionState.Inactive => KoanFactState.Observed,
                _ => KoanFactState.Rejected,
            };
            var summary = decision.State switch
            {
                SemanticDecisionState.Active when decision.Evidence is { Path.Length: > 1 } evidence =>
                    $"Koan activated the component through {string.Join(" -> ", evidence.Path)}.",
                SemanticDecisionState.Active => "Koan activated the component for this application.",
                SemanticDecisionState.Inactive => "Koan found the component but left it inactive because the application did not declare it.",
                _ => "Koan rejected the component while compiling the application constitution.",
            };

            return KoanFact.Create(
                code,
                kind,
                state,
                decision.Component.ToString(),
                summary,
                decision.Reason,
                problem?.Correction,
                decision.Evidence?.Source ?? "semantic-activation",
                $"semantic:{decision.Component.Value.ToLowerInvariant()}");
        }).ToArray();
    }
}
