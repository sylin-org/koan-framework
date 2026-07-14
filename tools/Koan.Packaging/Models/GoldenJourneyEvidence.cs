namespace Koan.Packaging.Models;

internal sealed record GoldenJourneyEvidence(
    string Lane,
    DateTimeOffset StartedAtUtc,
    double TotalSeconds,
    bool BusinessRuleObserved,
    bool PersistenceObserved,
    bool ReactiveWorkObserved,
    bool JobsCompositionObserved,
    bool FactsConverged,
    bool AgentBoundaryObserved,
    bool AgentMutationObserved,
    bool AdapterRejectionExplained,
    bool AdapterRecoveryObserved,
    IReadOnlyList<ApplicationStepEvidence> Steps);
