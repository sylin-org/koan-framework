namespace Koan.Data.Cutover;

public sealed record DefaultRouteTransitionPlan(
    string OperationId,
    DateTimeOffset PlannedAt,
    DefaultRouteDescriptor Source,
    DefaultRouteDescriptor Target,
    IReadOnlyList<DefaultRouteEntityPlan> Entities,
    IReadOnlyList<DefaultRouteTransitionBlocker> Blockers)
{
    public bool CanRun => Blockers.Count == 0;
}
