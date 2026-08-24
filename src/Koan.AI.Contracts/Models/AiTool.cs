using System.Text.Json;

namespace Koan.AI.Contracts.Models;

/// <summary>
/// A function a model may call natively. <see cref="ParametersSchema"/> is a JSON Schema object
/// (as a string) describing the arguments; providers that support native tool calling translate it
/// to their wire format.
/// </summary>
public sealed record AiToolDefinition(string Name, string? Description, string? ParametersSchema);

/// <summary>
/// One tool invocation requested by the model: the parsed form of a native
/// <c>message.tool_calls</c> entry (Ollama/OpenAI-style).
/// </summary>
public sealed record AiToolCall
{
    public required string Name { get; init; }

    /// <summary>Arguments as raw JSON text (object form when the provider supplied it).</summary>
    public required string ArgumentsJson { get; init; }

    /// <summary>Provider-supplied call identifier, when present.</summary>
    public string? Id { get; init; }

    public static AiToolCall FromJson(string name, JsonElement arguments, string? id = null)
        => new() { Name = name, ArgumentsJson = arguments.GetRawText(), Id = id };
}
