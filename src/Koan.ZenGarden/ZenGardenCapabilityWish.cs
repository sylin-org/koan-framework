namespace Koan.ZenGarden;

public sealed record ZenGardenCapabilityWish
{
    public required string RequestId { get; init; }
    public required string ToolFqid { get; init; }
    public required string OfferingSelector { get; init; }
    public IReadOnlyList<string> Requested { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Satisfied { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();
    public bool IsFulfilled { get; init; }
    public string Status { get; init; } = "requested";
    public string? Message { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? EventId { get; init; }
    public long? Cursor { get; init; }
}

public sealed class ZenGardenCapabilityWishOptions
{
    /// <summary>
    /// When true, Moss performs a dry run and does not execute mutations.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Optional capability type used when tokens are provided without an explicit type.
    /// Example: "model".
    /// </summary>
    public string? TypeHint { get; set; }
}

public sealed class ZenGardenCapabilityWatchOptions
{
    /// <summary>
    /// Emit a synthetic initial progress event from cached wish state after subscription registration.
    /// </summary>
    public bool EmitInitialState { get; set; } = true;
}
