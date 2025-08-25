namespace Sora.Media.Abstractions.Contracts;

public sealed record MediaTaskArg(string Name, string Type, bool Required, string? Default = null, IReadOnlyList<string>? Allowed = null, double? Min = null, double? Max = null, string? Pattern = null);