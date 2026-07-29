namespace Koan.Testing;

/// <summary>Complete cold/warm observation; interpretation belongs to its pinned provider fixture.</summary>
public sealed record DataBenchmarkObservation(
    DataBenchmarkFixture Fixture,
    string Cell,
    DataBenchmarkPhase Phase,
    TimeSpan Elapsed,
    long AllocatedBytes,
    int ProviderDispatches,
    long ProviderWork);
