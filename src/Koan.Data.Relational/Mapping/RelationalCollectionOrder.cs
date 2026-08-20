using System.Collections;
using System.Reflection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;

namespace Koan.Data.Relational.Mapping;

/// <summary>
/// Turns an order key that reaches through a collection into one SQL scalar, once, for the whole relational
/// family.
///
/// <para><c>-Sightings.LastChangedAt</c> orders widgets by their latest sighting. It is an aggregate over a
/// nested array rather than a field, so no binding exists for it — the mapping compiler stops at a collection
/// of objects, because such a collection has no single queryable physical path. Every relational runtime
/// therefore declined the key and left the framework to sort the whole result in memory.</para>
///
/// <para>The array does have a location: it lives at a known path inside the document column the root object
/// binding owns. This resolves that location and hands it to the dialect, which knows how to walk a JSON
/// array in its own SQL. Four runtimes, one rule, four grammars.</para>
/// </summary>
public static class RelationalCollectionOrder
{
    /// <summary>
    /// The complete ORDER BY term for <paramref name="sort"/>, or <see langword="null"/> when this mapping or
    /// dialect cannot express it and the framework should finish the ordering instead.
    /// </summary>
    public static string? Term(IRelationalMappingDialect dialect, MappingPlan mapping, SortSpec sort)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(sort);

        var max = sort.Aggregation switch
        {
            // None over a collection leaf means what it means to the in-memory sorter: take the maximum,
            // which is direction-agnostic and invents no element order.
            SortAggregation.Max or SortAggregation.None => true,
            SortAggregation.Min => false,
            // First/Last depend on an element order the document does not promise.
            _ => (bool?)null
        };
        if (max is null) return null;

        var path = sort.Path;
        var boundary = path.CollectionSegmentIndex;
        if (boundary <= 0 || boundary >= path.Members.Count) return null;

        // A second collection further along would need an aggregate of aggregates.
        for (var index = boundary; index < path.Members.Count; index++)
            if (IsCollection(ValueTypeOf(path.Members[index]))) return null;

        // Only a managed document has one root object to reach into; an explicit map may bind one logical
        // path across several physical ones, which is not a JSON array at a path.
        var root = mapping.Bindings.FirstOrDefault(binding =>
            binding.LogicalPath.IsRoot && binding.Shape == MappingValueShape.Object);
        if (root is null) return null;

        // Same rule the mapping compiler uses for every derived binding: physical segments are the member
        // names appended to the document root, so the array lands where the writer put it.
        var arrayPath = new PhysicalPath(
            root.PhysicalPath.Name,
            [.. root.PhysicalPath.Segments, .. path.Members.Take(boundary).Select(static member => member.Name)]);
        var collectionType = ValueTypeOf(path.Members[boundary - 1]);
        var arraySql = dialect.Read(arrayPath, MappingValueShape.Object, collectionType);

        return dialect.JsonArrayOrderTerm(
            arraySql,
            [.. path.Members.Skip(boundary).Select(static member => member.Name)],
            max.Value,
            sort.Desc,
            path.ValueType);
    }

    private static Type ValueTypeOf(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => typeof(object)
    };

    private static bool IsCollection(Type type) =>
        type != typeof(string) && type != typeof(byte[]) && typeof(IEnumerable).IsAssignableFrom(type);
}
