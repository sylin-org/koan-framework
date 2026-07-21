namespace Koan.Orchestration.Abstractions;

public sealed record ProviderStatus(string Provider, string EngineVersion, IReadOnlyList<(string Service, string State, string? Health)> Services);