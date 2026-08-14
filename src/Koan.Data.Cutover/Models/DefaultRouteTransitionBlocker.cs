namespace Koan.Data.Cutover;

public sealed record DefaultRouteTransitionBlocker(
    string Code,
    string Subject,
    string Reason,
    string Correction);
