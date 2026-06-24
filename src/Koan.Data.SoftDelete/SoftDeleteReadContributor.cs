using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Pipeline;

namespace Koan.Data.SoftDelete;

/// <summary>
/// The soft-delete read-visibility contributor (DATA-0106 seam): hides soft-deleted rows unless the ambient
/// <see cref="SoftDeleteAmbient.ShowDeleted"/> scope is active. The predicate is NULL-safe — a never-deleted row has
/// <c>__deleted</c> absent (the managed field is stamped only on delete), so "visible" is <c>__deleted IS NULL</c>
/// (<c>Exists=false</c>) OR <c>__deleted != true</c>. Both operators push down on relational + document adapters, so
/// the read fails closed (DATA-0106 §4b) on any store that cannot enforce it rather than residual-filtering deleted rows.
/// </summary>
internal sealed class SoftDeleteReadContributor : IReadFilterContributor
{
    // visible ⇔ __deleted absent (IS NULL) OR __deleted <> true.  Hidden ⇔ __deleted == true.
    private static readonly Filter HideDeleted = Filter.Any(
        Filter.On(FieldPath.Of("__deleted"), FilterOperator.Exists, FilterValue.Of(false)),
        Filter.On(FieldPath.Of("__deleted"), FilterOperator.Ne, FilterValue.Of(true)));

    public Filter? ReadFilter(Type entityType)
        => SoftDeleteMetadata.IsSoftDelete(entityType) && !SoftDeleteAmbient.ShowDeleted ? HideDeleted : null;

    // The adapter must push a managed-discriminator predicate at the store (RowScoped). On an in-memory-evaluating
    // adapter the __deleted managed field cannot be evaluated, so a [SoftDelete] entity there fails closed cleanly
    // ("does not announce") rather than throwing mid-read. Real stores (relational / Mongo) push __deleted to SQL/BSON.
    public Capability? RequiredCapability => DataCaps.Isolation.RowScoped;

    // A non-equality (viewer-state) predicate cannot be an equality cache-key segment ⇒ exclude soft-delete entities
    // from caching (DATA-0106 §5), else a cache hit could serve a soft-deleted row.
    public bool ExcludesFromCache(Type entityType) => SoftDeleteMetadata.IsSoftDelete(entityType);
}
