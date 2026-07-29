namespace Koan.Testing;

/// <summary>Pinned identity required before provider-relative observations can be compared.</summary>
public sealed record DataBenchmarkFixture(
    string Provider,
    string ProviderVersion,
    string DriverVersion,
    string Runner);
