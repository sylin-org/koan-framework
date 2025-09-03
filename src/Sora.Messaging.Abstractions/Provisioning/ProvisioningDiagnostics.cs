namespace Sora.Messaging.Provisioning;

public sealed record ProvisioningDiagnostics(
    string BusCode,
    string Provider,
    ProvisioningMode Mode,
    DesiredTopology Desired,
    CurrentTopology Current,
    TopologyDiff Diff,
    DateTimeOffset Timestamp,
    // Stable hash of Desired topology (structure only) for change detection across runs
    string? DesiredPlanHash = null,
    // Phase timings (ms) where available; 0 means not measured / skipped
    long PlanMs = 0,
    long InspectMs = 0,
    long DiffMs = 0,
    long ApplyMs = 0,
    int DesiredExchangeCount = 0,
    int DesiredQueueCount = 0,
    int DesiredBindingCount = 0,
    int DiffCreateExchangeCount = 0,
    int DiffCreateQueueCount = 0,
    int DiffCreateBindingCount = 0,
    int DiffUpdateExchangeCount = 0,
    int DiffUpdateQueueCount = 0,
    int DiffRemoveExchangeCount = 0,
    int DiffRemoveQueueCount = 0,
    int DiffRemoveBindingCount = 0,
    int AppliedExchangeCount = 0,
    int AppliedQueueCount = 0,
    int AppliedBindingCount = 0);