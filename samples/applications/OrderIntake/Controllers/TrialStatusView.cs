using OrderIntake.Domain;

namespace OrderIntake.Controllers;

/// <summary>The durable work item joined with its latest Jobs lifecycle state.</summary>
public sealed record TrialStatusView(
    string Id,
    WorkloadTarget Target,
    int RequestedOrderCount,
    string Status,
    double Progress,
    string? ProgressMessage,
    TrialReceipt? Receipt,
    string? Error,
    string? Correction);
