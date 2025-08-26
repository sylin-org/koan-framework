namespace Sora.Media.Abstractions.Contracts;

public sealed record MediaTaskStep(string Name, string Action, IReadOnlyDictionary<string, object?> SpecTemplate, string? Relationship = null, string? SavesTo = null, bool? ContinueOnError = null);