namespace Koan.Media.Abstractions.Contracts;

public record MediaTransformSpec(string Action, IReadOnlyDictionary<string, object?> Parameters);