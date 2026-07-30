using System.Collections;
using System.Globalization;
using System.Text;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Connector.Weaviate;

/// <summary>Fixed-schema metadata projection and exact Weaviate pre-filter writer.</summary>
internal static class WeaviateFilter
{
    internal static readonly FilterSupport Capabilities = FilterSupport.Uniform(
        nestedPaths: true,
        ignoreCase: false,
        FilterOperator.Eq,
        FilterOperator.Ne,
        FilterOperator.In,
        FilterOperator.Nin,
        FilterOperator.Has,
        FilterOperator.HasAny,
        FilterOperator.HasAll,
        FilterOperator.HasNone,
        FilterOperator.Size,
        FilterOperator.Exists);

    internal static IReadOnlyList<string> Project(DataObject? metadata)
    {
        if (metadata is null) return [];
        var terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in metadata.Properties)
            ProjectValue(terms, [property.Name], property.Value);
        return terms.Order(StringComparer.Ordinal).ToArray();
    }

    internal static string Write(Filter filter) => WriteNode(filter, negate: false);

    private static void ProjectValue(HashSet<string> terms, IReadOnlyList<string> path, object? value)
    {
        if (value is not null) terms.Add(Presence(path));
        terms.Add(Value(path, value));
        switch (value)
        {
            case DataObject data:
                foreach (var property in data.Properties)
                    ProjectValue(terms, [.. path, property.Name], property.Value);
                break;
            case DataArray array:
                terms.Add(Size(path, array.Items.Count));
                foreach (var item in array.Items) terms.Add(Element(path, item));
                break;
        }
    }

    private static string WriteNode(Filter filter, bool negate) => filter switch
    {
        AllOf all => Group(negate ? "Or" : "And", all.Operands.Select(item => WriteNode(item, negate))),
        AnyOf any => Group(negate ? "And" : "Or", any.Operands.Select(item => WriteNode(item, negate))),
        Not not => WriteNode(not.Operand, !negate),
        FieldFilter field => WriteLeaf(field, negate),
        _ => throw Unsupported(null, null, $"filter node '{filter.GetType().Name}'")
    };

    private static string WriteLeaf(FieldFilter field, bool negate)
    {
        if (field.IgnoreCase)
            throw Unsupported(field.Operator, field.Field.ToString(), "case-insensitive comparison");
        ValidatePath(field.Field.Segments);
        var scalar = Scalar(field);
        var set = Set(field);
        return field.Operator switch
        {
            FilterOperator.Eq when scalar is null => Contains(negate ? "ContainsAny" : "ContainsNone",
                [Presence(field.Field.Segments)]),
            FilterOperator.Eq => Contains(negate ? "ContainsNone" : "ContainsAny",
                [Value(field.Field.Segments, scalar)]),
            FilterOperator.Ne when scalar is null => Contains(negate ? "ContainsNone" : "ContainsAny",
                [Presence(field.Field.Segments)]),
            FilterOperator.Ne => Contains(negate ? "ContainsAny" : "ContainsNone",
                [Value(field.Field.Segments, scalar)]),
            FilterOperator.In => Contains(negate ? "ContainsNone" : "ContainsAny",
                set.Select(value => Value(field.Field.Segments, value))),
            FilterOperator.Nin => Contains(negate ? "ContainsAny" : "ContainsNone",
                set.Select(value => Value(field.Field.Segments, value))),
            FilterOperator.Has => Contains(negate ? "ContainsNone" : "ContainsAny",
                [Element(field.Field.Segments, scalar)]),
            FilterOperator.HasAny => Contains(negate ? "ContainsNone" : "ContainsAny",
                set.Select(value => Element(field.Field.Segments, value))),
            FilterOperator.HasAll when negate => Group("Or", set.Select(value =>
                Contains("ContainsNone", [Element(field.Field.Segments, value)]))),
            FilterOperator.HasAll => Contains("ContainsAll",
                set.Select(value => Element(field.Field.Segments, value))),
            FilterOperator.HasNone => Contains(negate ? "ContainsAny" : "ContainsNone",
                set.Select(value => Element(field.Field.Segments, value))),
            FilterOperator.Size => Contains(negate ? "ContainsNone" : "ContainsAny",
                [Size(field.Field.Segments, Count(scalar, field))]),
            FilterOperator.Exists => Exists(field, negate),
            _ => throw Unsupported(field.Operator, field.Field.ToString(), "operator")
        };
    }

    private static string Exists(FieldFilter field, bool negate)
    {
        var desired = Scalar(field) is not bool present || present;
        if (negate) desired = !desired;
        return Contains(desired ? "ContainsAny" : "ContainsNone", [Presence(field.Field.Segments)]);
    }

    private static string Group(string operation, IEnumerable<string> operands)
    {
        var values = operands.ToArray();
        if (values.Length == 0)
            throw new VectorFilterUnsupportedException(
                "weaviate", null, null, "Weaviate cannot translate an empty boolean filter group.");
        if (values.Length == 1) return values[0];
        return $"{{operator:{operation},operands:[{string.Join(',', values)}]}}";
    }

    private static string Contains(string operation, IEnumerable<string> terms)
    {
        var values = terms.Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length == 0)
            throw new VectorFilterUnsupportedException(
                "weaviate", null, null, "Weaviate cannot translate an empty filter value set.");
        return $"{{path:[\"{Infrastructure.Constants.Wire.Terms}\"],operator:{operation},valueText:[{string.Join(',', values.Select(value => $"\"{value}\""))}]}}";
    }

    private static string Presence(IReadOnlyList<string> path) => "p." + Path(path);
    private static string Value(IReadOnlyList<string> path, object? value) => "v." + Path(path) + "." + Token(value);
    private static string Element(IReadOnlyList<string> path, object? value) => "e." + Path(path) + "." + Token(value);
    private static string Size(IReadOnlyList<string> path, int value) =>
        "z." + Path(path) + "." + value.ToString(CultureInfo.InvariantCulture);

    private static string Path(IReadOnlyList<string> path) => Base64Url(Encoding.UTF8.GetBytes(
        string.Join('\u001f', path.Select(segment => segment.Length.ToString(CultureInfo.InvariantCulture) + ":" + segment))));

    private static string Token(object? value) => Base64Url(Encoding.UTF8.GetBytes(Canonical(value)));

    private static string Canonical(object? value) => value switch
    {
        null => "n",
        string text => "s:" + text,
        bool boolean => boolean ? "b:1" : "b:0",
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal =>
            "d:" + Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("G29", CultureInfo.InvariantCulture),
        Guid guid => "g:" + guid.ToString("D"),
        DateOnly date => "da:" + date.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly time => "ti:" + time.ToString("O", CultureInfo.InvariantCulture),
        DateTime date => "dt:" + date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => "do:" + date.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan duration => "du:" + duration.ToString("c", CultureInfo.InvariantCulture),
        byte[] bytes => "by:" + Convert.ToBase64String(bytes),
        DataArray array => "a:[" + string.Join(',', array.Items.Select(Canonical)) + "]",
        DataObject data => "o:{" + string.Join(',', data.Properties.Select(property =>
            property.Name.Length.ToString(CultureInfo.InvariantCulture) + ":" + property.Name + "=" + Canonical(property.Value))) + "}",
        IDictionary dictionary => "o:{" + string.Join(',', dictionary.Cast<DictionaryEntry>()
            .Select(entry => entry.Key?.ToString() is { } name
                ? name.Length.ToString(CultureInfo.InvariantCulture) + ":" + name + "=" + Canonical(entry.Value)
                : throw new VectorFilterUnsupportedException(
                    "weaviate", null, null, "Weaviate filter object keys must be strings."))) + "}",
        IEnumerable sequence => "a:[" + string.Join(',', sequence.Cast<object?>().Select(Canonical)) + "]",
        _ => throw new VectorFilterUnsupportedException(
            "weaviate", null, null,
            $"Weaviate cannot encode filter value type '{value.GetType().FullName}'.")
    };

    private static int Count(object? value, FieldFilter field)
    {
        try
        {
            var count = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (count < 0) throw new InvalidOperationException();
            return count;
        }
        catch (Exception error) when (error is FormatException or InvalidCastException or OverflowException or InvalidOperationException)
        {
            throw Unsupported(field.Operator, field.Field.ToString(), "non-negative integer size");
        }
    }

    private static object? Scalar(FieldFilter field) => field.Value switch
    {
        FilterValue.Scalar scalar => scalar.Value,
        FilterValue.Set set when set.Values.Count > 0 => set.Values[0],
        _ => null
    };

    private static IReadOnlyList<object?> Set(FieldFilter field) => field.Value switch
    {
        FilterValue.Set set => set.Values,
        FilterValue.Scalar scalar => [scalar.Value],
        _ => []
    };

    private static void ValidatePath(IReadOnlyList<string> path)
    {
        if (path.Count == 0 || path.Any(string.IsNullOrWhiteSpace))
            throw Unsupported(null, string.Join('.', path), "empty metadata path");
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static VectorFilterUnsupportedException Unsupported(
        FilterOperator? operation,
        string? path,
        string reason) => new(
        "weaviate",
        operation,
        path,
        $"Weaviate cannot translate {reason}" +
        (operation is null ? string.Empty : $" '{operation}'") +
        (path is null ? string.Empty : $" on metadata field '{path}'") + ".");
}
