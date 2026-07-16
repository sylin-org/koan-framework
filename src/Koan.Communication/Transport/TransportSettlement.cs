namespace Koan.Communication;

/// <summary>Bounded terminal counts for one locally observable Transport operation.</summary>
public sealed record TransportSettlement(
    Guid OperationId,
    long Expected,
    long Delivered,
    long Filtered,
    long Failed)
{
    public bool Succeeded => Failed == 0;
}
