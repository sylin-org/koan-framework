using System.Collections.Generic;

namespace Koan.Data.Core.Events;

/// <summary>
/// Summarises the outcomes produced by lifecycle hooks during batch execution.
/// </summary>
public sealed record EntityBatchResult(EntityBatchDisposition Disposition, IReadOnlyList<EntityOutcome> Outcomes)
{
    public static EntityBatchResult Success(IReadOnlyList<EntityOutcome> outcomes)
        => new(EntityBatchDisposition.Success, outcomes);

    public static EntityBatchResult Partial(IReadOnlyList<EntityOutcome> outcomes)
        => new(EntityBatchDisposition.PartialSuccess, outcomes);

    public static EntityBatchResult Cancelled(IReadOnlyList<EntityOutcome> outcomes)
        => new(EntityBatchDisposition.Cancelled, outcomes);
}
