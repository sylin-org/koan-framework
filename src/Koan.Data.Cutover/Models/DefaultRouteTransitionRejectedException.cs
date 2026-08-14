namespace Koan.Data.Cutover;

public sealed class DefaultRouteTransitionRejectedException : InvalidOperationException
{
    public DefaultRouteTransitionRejectedException(DefaultRouteTransitionPlan plan)
        : base(
            $"Default-route promotion to '{plan.Target.Source}' was rejected by {plan.Blockers.Count} preflight blocker(s). " +
            "Apply every reported correction and plan again.")
    {
        Plan = plan;
    }

    public DefaultRouteTransitionPlan Plan { get; }
}
