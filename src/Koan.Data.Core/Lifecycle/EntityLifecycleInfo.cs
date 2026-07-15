namespace Koan.Data.Core.Lifecycle;

/// <summary>Inspectable host composition for one entity lifecycle plan.</summary>
public sealed record EntityLifecycleInfo(
    string EntityType,
    IReadOnlyDictionary<string, int> HandlerCounts)
{
    public int TotalHandlers => HandlerCounts.Values.Sum();
}
