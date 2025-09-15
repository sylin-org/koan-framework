namespace Koan.Data.Abstractions.Instructions;

public sealed record Instruction(
    string Name,
    object? Payload = null,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    IReadOnlyDictionary<string, object?>? Options = null
);