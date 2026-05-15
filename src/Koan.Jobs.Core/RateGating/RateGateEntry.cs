namespace Koan.Jobs.RateGating;

/// <summary>
/// Snapshot of an active host-rate gate. Returned by <see cref="IHostRateGate"/> queries for
/// observability and admin surfaces.
/// </summary>
public sealed record RateGateEntry(
    string HostTag,
    DateTimeOffset SetAt,
    DateTimeOffset ReleaseAt,
    string Reason);
