namespace Koan.Web.Infrastructure;

public sealed class PaginationSafetyBounds
{
    public int MinPageSize { get; set; } = 1;
    public int MaxPageSize { get; set; } = KoanWebConstants.Defaults.MaxPageSize;
    public int AbsoluteMaxRecords { get; set; } = 10_000;

    public static PaginationSafetyBounds Default => new()
    {
        MinPageSize = 1,
        MaxPageSize = KoanWebConstants.Defaults.MaxPageSize,
        AbsoluteMaxRecords = 10_000
    };

    public PaginationSafetyBounds Clone() => new()
    {
        MinPageSize = MinPageSize,
        MaxPageSize = MaxPageSize,
        AbsoluteMaxRecords = AbsoluteMaxRecords
    };
}
