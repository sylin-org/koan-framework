namespace Koan.Testing;

/// <summary>One reusable lifecycle/fault module and the exact primer cells it can evidence.</summary>
public sealed record DataScenarioDefinition(
    string Id,
    DataScenarioKind Kind,
    IReadOnlyList<string> AcceptanceIds,
    bool RequiresLiveProvider,
    bool RequiresSecondHost,
    bool RequiresRestart,
    int MinimumOperations);
