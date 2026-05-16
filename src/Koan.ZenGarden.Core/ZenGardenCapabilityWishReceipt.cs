namespace Koan.ZenGarden.Core;

/// <summary>
/// Non-blocking capability wish receipt returned by initialization provider.
/// </summary>
public sealed record ZenGardenCapabilityWishReceipt
{
    public required string RequestId { get; init; }
    public required string ToolFqid { get; init; }
    public required string OfferingSelector { get; init; }
    public IReadOnlyList<string> Requested { get; init; } = [];
    public IReadOnlyList<string> Missing { get; init; } = [];
    public bool IsFulfilled { get; init; }
    public string Status { get; init; } = "requested";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
