using Koan.ZenGarden.Models;

namespace Koan.ZenGarden;

public enum ZenGardenAvailabilityEventKind
{
    Online = 0,
    Offline = 1,
    Changed = 2,
    CapabilitiesSatisfied = 3,
    CapabilitiesUnsatisfied = 4
}

public sealed record ZenGardenAvailabilityEvent
{
    public required ZenGardenAvailabilityEventKind Kind { get; init; }
    public required ZenGardenToolSnapshot Current { get; init; }
    public ZenGardenToolSnapshot? Previous { get; init; }
    public string? EventId { get; init; }
    public long? Cursor { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
