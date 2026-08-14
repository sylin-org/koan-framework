namespace Koan.Data.Cutover;

public sealed record DefaultRouteEntityPlan(
    string RootIdentity,
    string RootType,
    DefaultRouteEntityDisposition Disposition,
    string SourceContainer,
    string TargetContainer,
    bool SourceContainerPresent,
    IReadOnlyList<DefaultRouteTransitionBlocker> Blockers);
