namespace Koan.Packaging.Models;

internal sealed record GoldenJourneyEvidence(
    string Lane,
    DateTimeOffset StartedAtUtc,
    double TotalSeconds,
    bool CompositionLockfileObserved,
    bool CompositionLockfileMatched,
    bool BusinessRuleObserved,
    bool PersistenceObserved,
    bool ReactiveWorkObserved,
    bool JobsCompositionObserved,
    bool FactsConverged,
    bool AgentBoundaryObserved,
    bool AgentMutationObserved,
    bool AdapterRejectionExplained,
    bool AdapterRejectionAffectedReadiness,
    bool RejectedWorkerLogsCalm,
    bool AdapterRecoveryObserved,
    IReadOnlyList<ApplicationStepEvidence> Steps);
