using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Semantics;
using Koan.Data.Relational;

namespace Koan.Data.Connector.Firebird.Runtime;

/// <summary>
/// One Firebird connection opens exactly one database, so a table carries no qualifier. Scalars are
/// written in DATA-0100's order-preserving form, which is also what keeps DateTimeOffset, DateOnly,
/// TimeOnly and TimeSpan bindable — the FirebirdClient accepts none of those CLR types natively and
/// the column types in the DDL executor are chosen to match the encoded form.
/// </summary>
internal sealed class FirebirdEntityPlan<TEntity, TKey>(
    MappingPlan mapping,
    DataSegmentationPlan segmentation)
    : RelationalEntityPlan<TEntity, TKey, FirebirdDialect>(
        mapping,
        segmentation,
        new FirebirdDialect(),
        qualifier: null)
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>Every physical name is quoted, so reads resolve through the plain quoted column.</summary>
    protected override string Project(string root) => FirebirdDialect.Quote(root);

    /// <summary>
    /// The encoded form the filter path compares against, applied at the one seam so writes and
    /// filters can never disagree: DateTimeOffset to UTC ISO text, TimeSpan to ticks, DateOnly and
    /// TimeOnly to fixed text. Everything else binds natively.
    /// </summary>
    protected override object? EncodeScalar(object? value) => ComparableScalarEncoding.EncodeComparand(value);

    /// <summary>
    /// The shadow columns this entity's table carries beside the document: every top-level scalar the
    /// mapping stores inside the JSON document (so a filter, a sort or an index can still be answered
    /// by the store on a engine with no JSON functions), plus the framework-managed isolation
    /// discriminators the same read path must reach.
    /// </summary>
    public IReadOnlyList<FirebirdShadowColumn> ShadowColumns { get; } = CompileShadowColumns(mapping, segmentation);

    private static FirebirdShadowColumn[] CompileShadowColumns(MappingPlan mapping, DataSegmentationPlan segmentation)
    {
        var scalars = mapping.Bindings
            .Where(static binding => binding.PhysicalPath.IsNested &&
                                     binding.PhysicalPath.Segments.Count == 1 &&
                                     binding.Shape == MappingValueShape.Scalar)
            .GroupBy(static binding => binding.PhysicalPath.Segments[0], StringComparer.Ordinal)
            .Select(static group => new FirebirdShadowColumn(group.Key, group.First().PhysicalType, Managed: false));
        var managed = ManagedFieldRegistry.ForType(mapping.EntityType).Select(static field => field.StorageName)
            .Concat(segmentation.For(mapping.EntityType).Fields.Select(static field => field.StorageName))
            .Distinct(StringComparer.Ordinal)
            .Select(static name => new FirebirdShadowColumn(name, typeof(string), Managed: true));
        return managed.Concat(scalars).ToArray();
    }

    /// <summary>
    /// The shadow-column values for one entity, in the encoded form the write plan and the filter
    /// path share. The write plan itself carries only the document root, so the mirror is composed
    /// here, from the same bindings the reads resolve through.
    /// </summary>
    public IReadOnlyList<(string Name, object? Value)> ShadowValues(object entity) =>
        Mapping.Bindings
            .Where(static binding => binding.PhysicalPath.IsNested &&
                                     binding.PhysicalPath.Segments.Count == 1 &&
                                     binding.Shape == MappingValueShape.Scalar)
            .Select(binding => (binding.PhysicalPath.Segments[0], EncodeScalar(binding.Read(entity))))
            .ToArray();
}
