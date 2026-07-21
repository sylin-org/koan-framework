namespace Koan.Communication;

/// <summary>Bounded terminal counts for one locally observable Entity Event operation.</summary>
public sealed record EventSettlement(
    Guid OperationId,
    long Expected,
    long Delivered,
    long Filtered,
    long Failed)
{
    public bool Succeeded => Failed == 0;
}
