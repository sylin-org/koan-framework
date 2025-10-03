namespace Koan.AI.Contracts.Models;

/// <summary>
/// Represents a structured part of a message, allowing adapters to transport
/// mixed content (plain text, JSON payloads, tool arguments, citations, etc.).
/// </summary>
public sealed record AiMessagePart
{
    /// <summary>
    /// Logical type of the part (e.g., "text", "json", "tool-call").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Textual representation of the part when applicable.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Arbitrary structured payload; adapters should serialize according to <see cref="Type"/> and
    /// <see cref="MimeType"/>.
    /// </summary>
    public object? Data { get; init; }

    /// <summary>
    /// Optional MIME type that clarifies how <see cref="Data"/> should be interpreted.
    /// </summary>
    public string? MimeType { get; init; }
}
