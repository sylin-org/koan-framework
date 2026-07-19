namespace Koan.Orchestration.Abstractions;

public sealed record RunOptions(bool Detach, TimeSpan? ReadinessTimeout);