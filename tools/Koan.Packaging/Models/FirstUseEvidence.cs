namespace Koan.Packaging.Models;

internal sealed record FirstUseEvidence(
    string Lane,
    string Runtime,
    string OperatingSystem,
    DateTimeOffset StartedAtUtc,
    double TotalSeconds,
    bool CompositionLockfileObserved,
    bool CompositionLockfileMatched,
    string SelectedAdapter,
    bool RestFilterObserved,
    bool StartupReported,
    bool McpSchemaWarningsAbsent,
    bool FactsConverged,
    bool DryRunPreservedState,
    bool AgentMutationObserved,
    bool RemoteDeleteHidden,
    IReadOnlyList<ApplicationStepEvidence> Steps);
