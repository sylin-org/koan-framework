using System.Collections.Generic;

namespace Koan.AI.Contracts.Models;

/// <summary>
/// Represents a conversation message exchanged with an AI adapter.
/// Role and content are the constructor; richer metadata (name, tool call identifiers, structured
/// parts) is supplied through object initializers. A provider that understands only plain text reads
/// <see cref="Content"/>, while the builder and adapters translate structured parts where the
/// provider supports them.
/// </summary>
public record class AiMessage
{
    public AiMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }

    public string Role { get; init; }
    public string Content { get; init; }

    /// <summary>
    /// Optional author or tool name (e.g., tool function identifier when the role is <c>tool</c>).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Optional call identifier supplied by tool-invocation capable models.
    /// </summary>
    public string? ToolCallId { get; init; }

    /// <summary>
    /// Structured content parts (text, JSON, binary). When supplied, adapters should prefer these parts
    /// over the legacy <see cref="Content"/> property. Callers should still provide <see cref="Content"/>
    /// for backwards compatibility with adapters that only support text prompts.
    /// </summary>
    public IReadOnlyList<AiMessagePart>? Parts { get; init; }

    /// <summary>
    /// Arbitrary metadata that augmentations or applications can attach to individual turns.
    /// </summary>
    public IDictionary<string, string>? Metadata { get; init; }
}