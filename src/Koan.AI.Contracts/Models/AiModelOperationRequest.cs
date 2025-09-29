using System.Collections.Generic;

namespace Koan.AI.Contracts.Models;

/// <summary>
/// Describes a model-management action (install, refresh, flush) requested against an AI adapter.
/// </summary>
public sealed record AiModelOperationRequest
{
    /// <summary>The logical model identifier (e.g., "llama3", "mistral:7b").</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Optional version tag when the model identifier does not already include one.</summary>
    public string? Version { get; init; }

    /// <summary>Optional repository or namespace prefix (e.g., "registry.example.com/models").</summary>
    public string? Repository { get; init; }

    /// <summary>Optional provenance metadata describing who/what triggered the action.</summary>
    public AiModelProvenance? Provenance { get; init; }

    /// <summary>Provider-specific parameters (e.g., insecure transport flags).</summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
}
