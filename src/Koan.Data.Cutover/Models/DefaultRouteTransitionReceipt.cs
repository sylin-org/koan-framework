namespace Koan.Data.Cutover;

public sealed record DefaultRouteTransitionReceipt(
    string OperationId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    DefaultRouteDescriptor Previous,
    DefaultRouteDescriptor Active,
    IReadOnlyList<DefaultRouteEntityReceipt> Entities);
