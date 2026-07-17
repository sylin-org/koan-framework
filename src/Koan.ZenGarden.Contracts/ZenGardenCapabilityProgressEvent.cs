namespace Koan.ZenGarden;

public enum ZenGardenCapabilityProgressEventKind
{
    Requested = 0,
    InProgress = 1,
    PartiallyFulfilled = 2,
    Fulfilled = 3,
    Failed = 4
}

public sealed record ZenGardenCapabilityProgressEvent
{
    public required ZenGardenCapabilityProgressEventKind Kind { get; init; }
    public required ZenGardenCapabilityWish Wish { get; init; }
    public ZenGardenCapabilityWish? Previous { get; init; }
    public Models.ZenGardenToolSnapshot? CurrentTool { get; init; }
    public string? EventId { get; init; }
    public long? Cursor { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
