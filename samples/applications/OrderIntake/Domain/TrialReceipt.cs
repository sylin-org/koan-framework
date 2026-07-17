namespace OrderIntake.Domain;

/// <summary>Exact evidence for one workload on one runtime; never a provider ranking.</summary>
public sealed record TrialReceipt
{
    public required WorkloadTarget Target { get; init; }
    public required string Provider { get; init; }
    public required IReadOnlyList<string> Capabilities { get; init; }
    public required int Requested { get; init; }
    public required int Written { get; init; }
    public required int ReadBack { get; init; }
    public required int Verified { get; init; }
    public required int Removed { get; init; }
    public required TimeSpan WriteDuration { get; init; }
    public required TimeSpan ReadDuration { get; init; }
    public required TimeSpan CleanupDuration { get; init; }
    public required TimeSpan TotalDuration { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required string Framework { get; init; }
    public required string OperatingSystem { get; init; }
    public required string Architecture { get; init; }
}
