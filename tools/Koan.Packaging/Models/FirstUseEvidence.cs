namespace Koan.Packaging.Models;

internal sealed record FirstUseEvidence(
    string Lane,
    string Runtime,
    string OperatingSystem,
    DateTimeOffset StartedAtUtc,
    double TotalSeconds,
    string SelectedAdapter,
    bool StartupReported,
    bool FactsConverged,
    bool DryRunPreservedState,
    bool AgentMutationObserved,
    bool RemoteDeleteHidden,
    IReadOnlyList<ApplicationStepEvidence> Steps);
