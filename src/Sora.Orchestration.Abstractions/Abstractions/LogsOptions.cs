namespace Sora.Orchestration.Abstractions;

public sealed record LogsOptions(string? Service, bool Follow, int? Tail, string? Since = null);