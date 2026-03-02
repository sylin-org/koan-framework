namespace Koan.ZenGarden.Models;

public enum ZenGardenToolType
{
    Unknown = 0,
    Offering = 1,
    SeedBank = 2
}

public enum ZenGardenToolState
{
    Unknown = 0,
    Ready = 1,
    Degraded = 2,
    Unavailable = 3
}

public sealed record ZenGardenConnection
{
    public string? Protocol { get; init; }
    public string? Hostname { get; init; }
    public string? Ip { get; init; }
    public int? Port { get; init; }
    public IReadOnlyList<string> Uris { get; init; } = Array.Empty<string>();
}

public sealed record ZenGardenToolSnapshot
{
    /// <summary>
    /// Bare fqid: "mongodb", "mongodb:prod", "ollama:adopted".
    /// </summary>
    public required string ToolFqid { get; init; }

    /// <summary>
    /// Unique identifier (GUIDv7) from tool.id.
    /// </summary>
    public string? ToolUid { get; init; }

    /// <summary>
    /// Offering type from tool.type: "mongodb", "ollama", "seed-bank".
    /// </summary>
    public string? OfferingType { get; init; }

    /// <summary>
    /// Tool category: "orchestrator", "offering", "storage".
    /// Maps to <see cref="ZenGardenToolType"/> for backward-compat.
    /// </summary>
    public string? Category { get; init; }

    public ZenGardenToolType ToolType { get; init; }
    public ZenGardenToolState State { get; init; }
    public bool Ready { get; init; }
    public long Revision { get; init; }
    public string? StoneId { get; init; }
    public string? StoneName { get; init; }
    public string? StoneEndpoint { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
    public ZenGardenConnection? Connection { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Capabilities { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
    public long? CapabilityRevision { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
