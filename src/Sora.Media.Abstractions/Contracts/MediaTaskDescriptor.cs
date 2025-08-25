namespace Sora.Media.Abstractions.Contracts;

public sealed record MediaTaskDescriptor(string Code, int Version, string? Title, string? Summary, IReadOnlyList<MediaTaskArg> Args, IReadOnlyList<MediaTaskStep> Steps, IReadOnlyList<string>? Requires);