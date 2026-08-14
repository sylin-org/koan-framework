namespace Koan.Data.Cutover;

public sealed class DefaultRouteTransitionException : InvalidOperationException
{
    internal DefaultRouteTransitionException(
        string operationId,
        string targetSource,
        bool targetMayContainData,
        Exception inner)
        : base(
            $"Default-route promotion '{operationId}' to '{targetSource}' failed. " +
            (targetMayContainData
                ? "The target may contain partial data and is quarantined; empty or reprovision it before retrying."
                : "The active route is unchanged and the target was not mutated."),
            inner)
    {
        OperationId = operationId;
        TargetSource = targetSource;
        TargetMayContainData = targetMayContainData;
    }

    public string OperationId { get; }
    public string TargetSource { get; }
    public bool TargetMayContainData { get; }
}
