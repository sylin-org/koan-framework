namespace Sora.Orchestration.Abstractions;

public sealed record RunOptions(bool Detach, TimeSpan? ReadinessTimeout);