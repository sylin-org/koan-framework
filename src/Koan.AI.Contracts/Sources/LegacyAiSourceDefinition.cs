using System.Collections.Generic;

namespace Koan.AI.Contracts.Sources;

/// <summary>
/// DEPRECATED: Old AiSourceDefinition (inverted terminology - this was actually a member).
/// Kept temporarily for migration. Will be removed.
/// Use AiMemberDefinition instead.
/// </summary>
[System.Obsolete("Use AiMemberDefinition - this represented an endpoint, not a source collection")]
public sealed record LegacyAiSourceDefinition
{
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public string? ConnectionString { get; init; }
    public string? Group { get; init; }
    public int Priority { get; init; } = 50;
    public IReadOnlyDictionary<string, AiCapabilityConfig> Capabilities { get; init; }
        = new Dictionary<string, AiCapabilityConfig>();
    public IReadOnlyDictionary<string, string> Settings { get; init; }
        = new Dictionary<string, string>();
    public string? Origin { get; init; }
    public bool IsAutoDiscovered { get; init; }
}
