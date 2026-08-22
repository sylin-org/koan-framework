namespace Koan.Web.Infrastructure;

public sealed class PaginationSafetyBounds
{
    public const string SectionPath = "Koan:Web:Pagination";

    public int MinPageSize { get; set; } = 1;
    public int MaxPageSize { get; set; } = KoanWebConstants.Defaults.MaxPageSize;
    public int AbsoluteMaxRecords { get; set; } = 10_000;

    public static PaginationSafetyBounds Default => new()
    {
        MinPageSize = 1,
        MaxPageSize = KoanWebConstants.Defaults.MaxPageSize,
        AbsoluteMaxRecords = 10_000
    };

    /// <summary>
    /// Forces the three bounds into a consistent order, so a partial or contradictory configuration
    /// still yields usable limits rather than rejecting the application at startup.
    /// </summary>
    public void Normalize()
    {
        MinPageSize = Math.Max(MinPageSize, 1);
        MaxPageSize = Math.Clamp(MaxPageSize, MinPageSize, 1_000);
        AbsoluteMaxRecords = Math.Max(AbsoluteMaxRecords, MaxPageSize);
    }

    public PaginationSafetyBounds Clone() => new()
    {
        MinPageSize = MinPageSize,
        MaxPageSize = MaxPageSize,
        AbsoluteMaxRecords = AbsoluteMaxRecords
    };
}
