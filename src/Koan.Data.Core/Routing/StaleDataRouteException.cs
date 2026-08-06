namespace Koan.Data.Core.Routing;

public sealed class StaleDataRouteException : InvalidOperationException
{
    internal StaleDataRouteException(
        string staleSource,
        string staleRoute,
        long staleGeneration,
        string activeSource,
        string activeRoute,
        long activeGeneration)
        : base(
            $"The retained Data handle for source '{staleSource}' is stale " +
            $"({staleRoute}, generation {staleGeneration}); the active binding is source '{activeSource}' " +
            $"({activeRoute}, generation {activeGeneration}). Reacquire the Entity repository or transaction and retry.")
    {
        StaleSource = staleSource;
        StaleRouteIdentity = staleRoute;
        StaleGeneration = staleGeneration;
        ActiveSource = activeSource;
        ActiveRouteIdentity = activeRoute;
        ActiveGeneration = activeGeneration;
    }

    public string StaleSource { get; }
    public string StaleRouteIdentity { get; }
    public long StaleGeneration { get; }
    public string ActiveSource { get; }
    public string ActiveRouteIdentity { get; }
    public long ActiveGeneration { get; }
}
