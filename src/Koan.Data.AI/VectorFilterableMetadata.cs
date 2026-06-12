using System.Collections;
using System.Reflection;
using Koan.Data.AI.Attributes;

namespace Koan.Data.AI;

/// <summary>
/// Extracts an entity's <b>filterable facets</b> as vector metadata keyed by CLR property name
/// (AI-0036 §9 D1). Stamping these at embed-write time is what makes the lambda filter DX sound by
/// construction: <c>Chain.Retrieve&lt;Doc&gt;(q, filter: d =&gt; d.Year &gt; 2020)</c> lowers to a metadata
/// key <c>"Year"</c> that this method guarantees exists on the stored vector.
/// </summary>
/// <remarks>
/// Zero-setup, sane defaults: every short scalar/string/enum/date/guid property and string
/// collection is stamped automatically — no attributes required. Excluded by default: <c>Id</c> (the
/// vector key), anything marked <c>[EmbeddingIgnore]</c>, complex/nested objects, and <b>long
/// strings</b> (over <see cref="MaxFacetLength"/> chars — those are content, not facets, and stamping
/// them would bloat every vector record). A future <c>[VectorFilterIgnore]</c> can refine this.
/// </remarks>
public static class VectorFilterableMetadata
{
    /// <summary>Strings longer than this are treated as content (not a filterable facet) and skipped.</summary>
    public const int MaxFacetLength = 512;

    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "Id" };

    public static IReadOnlyDictionary<string, object>? Extract(object? entity)
    {
        if (entity is null) return null;

        Dictionary<string, object>? bag = null;
        foreach (var prop in entity.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
            if (Excluded.Contains(prop.Name)) continue;
            if (prop.GetCustomAttribute<EmbeddingIgnoreAttribute>() is not null) continue;

            object? value;
            try { value = prop.GetValue(entity); }
            catch { continue; }
            if (value is null) continue;

            if (!TryFacet(value, out var facet)) continue;

            (bag ??= new Dictionary<string, object>())[prop.Name] = facet!;
        }

        return bag;
    }

    private static bool TryFacet(object value, out object? facet)
    {
        facet = null;
        switch (value)
        {
            case string s:
                if (s.Length > MaxFacetLength) return false;
                facet = s; return true;
            case bool or sbyte or byte or short or ushort or int or uint or long or ulong
                or float or double or decimal:
                facet = value; return true;
            case Enum e:
                facet = e.ToString(); return true;
            case DateTime or DateTimeOffset or DateOnly or TimeOnly or Guid:
                facet = value; return true;
            case IEnumerable<string> strings:
                var list = strings.Where(x => x is not null && x.Length <= MaxFacetLength).ToArray();
                if (list.Length == 0) return false;
                facet = list; return true;
            case IEnumerable enumerable: // arrays/lists of scalars or enums (string handled above)
                var items = new List<object>();
                foreach (var item in enumerable)
                {
                    if (item is null) continue;
                    // each element must itself be a scalar/string facet, not a nested collection
                    if (item is not string && item is IEnumerable) return false;
                    if (!TryFacet(item, out var f) || f is null) return false;
                    items.Add(f);
                }
                if (items.Count == 0) return false;
                facet = items.ToArray(); return true;
            default:
                return false; // complex/nested object -> not a filterable facet
        }
    }
}
