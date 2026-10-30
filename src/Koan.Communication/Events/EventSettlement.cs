namespace Koan.Communication;

/// <summary>Bounded terminal counts for one locally observable Entity Event operation.</summary>
/// <param name="OperationId">The operation these counts settle.</param>
/// <param name="Expected">Accepted subscription-group deliveries: every one settles exactly once.</param>
/// <param name="Delivered">Deliveries whose handler completed.</param>
/// <param name="Filtered">Deliveries the handler's <c>Where</c> declined.</param>
/// <param name="Failed">
/// Deliveries that did not complete: the handler threw, exceeded the handler timeout, or was stopped with the host,
/// or the subscription group's backlog was over its memory budget. Includes <paramref name="Overflowed"/>.
/// </param>
/// <param name="Overflowed">
/// Failed deliveries that never reached their handler because the publications already waiting for that subscription
/// group held its memory budget. Only that group fails; the publisher and every other group are unaffected.
/// </param>
public sealed record EventSettlement(
    Guid OperationId,
    long Expected,
    long Delivered,
    long Filtered,
    long Failed,
    long Overflowed)
{
    public bool Succeeded => Failed == 0;
}
