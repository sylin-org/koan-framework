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
    public required string ToolFqid { get; init; }
    public string? ToolUid { get; init; }
    public ZenGardenToolType ToolType { get; init; }
    public ZenGardenToolState State { get; init; }
    public bool Ready { get; init; }
    public long Revision { get; init; }
    public string? StoneId { get; init; }
    public string? StoneName { get; init; }
    public ZenGardenConnection? Connection { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Capabilities { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
    public long? CapabilityRevision { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
