using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Connector.RedisVector;

internal static class RedisVectorFilter
{
    internal static readonly FilterSupport Capabilities = FilterSupport.Uniform(
        nestedPaths: true,
        ignoreCase: false,
        FilterOperator.Eq, FilterOperator.Ne,
        FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
        FilterOperator.In, FilterOperator.Nin,
        FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll, FilterOperator.HasNone,
        FilterOperator.Size, FilterOperator.Exists);

    internal static RedisVectorProjection Project(
        DataObject? metadata,
        DataObject managedValues,
        int maxPaths)
    {
        if (metadata is null) return RedisVectorProjection.Empty;
        ArgumentNullException.ThrowIfNull(managedValues);
        var managedFields = managedValues.Properties
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);
        var scalar = new HashSet<string>(StringComparer.Ordinal);
        var elements = new HashSet<string>(StringComparer.Ordinal);
        var unordered = new HashSet<string>(StringComparer.Ordinal);
        var dynamic = new Dictionary<string, string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);

        VisitObject(metadata, [], managedFields, present, scalar, elements, unordered, dynamic, paths, maxPaths);
        return new RedisVectorProjection(
            Join(present),
            Join(scalar),
            Join(elements),
            Join(unordered),
            dynamic.OrderBy(static item => item.Key, StringComparer.Ordinal).ToArray(),
            paths.OrderBy(static path => path, StringComparer.Ordinal).ToArray());
    }

    internal static RedisVectorCompiledFilter Compile(Filter? filter, IReadOnlySet<string> attributes)
    {
        if (filter is null) return RedisVectorCompiledFilter.True;
        return new Compiler(attributes).Write(filter);
    }

    internal static IReadOnlySet<string> RequiredDynamicFields(Filter? filter)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        AddRequired(filter, fields);
        return fields;
    }

    internal static IReadOnlySet<string> RequiredOrderedPaths(Filter? filter) =>
        RequiredDynamicFields(filter)
            .Where(field => field.StartsWith(Infrastructure.Constants.Wire.NumberPrefix, StringComparison.Ordinal))
            .Select(static field => field[2..])
            .ToHashSet(StringComparer.Ordinal);

    internal static VectorFilterUnsupportedException UnorderedComparison(IEnumerable<string> paths) =>
        Unsupported(null, string.Join(',', paths.Order(StringComparer.Ordinal)),
            "numeric comparison over metadata values that Redis NUMERIC cannot represent exactly");

    private static void AddRequired(Filter? filter, ISet<string> fields)
    {
        switch (filter)
        {
            case null:
                return;
            case AllOf all:
                foreach (var operand in all.Operands) AddRequired(operand, fields);
                return;
            case AnyOf any:
                foreach (var operand in any.Operands) AddRequired(operand, fields);
                return;
            case Not not:
                AddRequired(not.Operand, fields);
                return;
            case FieldFilter field when field.Operator is
                FilterOperator.Gt or FilterOperator.Gte or FilterOperator.Lt or FilterOperator.Lte:
                fields.Add(Infrastructure.Constants.Wire.NumberPrefix + PathHash(field.Field.Segments));
                return;
            case FieldFilter field when field.Operator == FilterOperator.Size:
                fields.Add(Infrastructure.Constants.Wire.SizePrefix + PathHash(field.Field.Segments));
                return;
        }
    }

    internal static string PathHash(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0 || segments.Any(static segment => string.IsNullOrWhiteSpace(segment)))
            throw Unsupported(null, string.Join('.', segments), "empty metadata path");
        var value = new StringBuilder();
        foreach (var segment in segments)
            value.Append(segment.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(segment).Append(';');
        return Hex(value.ToString());
    }

    private static void VisitObject(
        DataObject data,
        IReadOnlyList<string> prefix,
        IReadOnlySet<string> managedFields,
        ISet<string> present,
        ISet<string> scalar,
        ISet<string> elements,
        ISet<string> unordered,
        IDictionary<string, string> dynamic,
        ISet<string> paths,
        int maxPaths)
    {
        foreach (var property in data.Properties)
        {
            if (property.Name.StartsWith("__koan", StringComparison.OrdinalIgnoreCase) &&
                (prefix.Count != 0 || !managedFields.Contains(property.Name)))
                throw new InvalidOperationException(
                    $"RedisVector metadata key '{property.Name}' is reserved for Koan-managed values.");
            var path = prefix.Concat([property.Name]).ToArray();
            var pathHash = PathHash(path);
            if (paths.Add(pathHash) && paths.Count > maxPaths)
                throw new InvalidOperationException(
                    $"RedisVector metadata contains more than the configured {maxPaths} indexed paths.");
            if (property.Value is not null) present.Add(pathHash);
            switch (property.Value)
            {
                case null:
                    break;
                case DataObject nested:
                    VisitObject(nested, path, managedFields, present, scalar, elements, unordered, dynamic, paths, maxPaths);
                    break;
                case DataArray array:
                    dynamic[Infrastructure.Constants.Wire.SizePrefix + pathHash] =
                        array.Items.Count.ToString(CultureInfo.InvariantCulture);
                    foreach (var item in array.Items)
                        if (TryCanonical(item, out var token))
                            elements.Add(ValueToken(pathHash, token));
                    break;
                default:
                    if (!TryCanonical(property.Value, out var canonical))
                    {
                        if (IsNumeric(property.Value)) unordered.Add(pathHash);
                        break;
                    }
                    scalar.Add(ValueToken(pathHash, canonical));
                    if (TryNumericOrder(property.Value, out var number))
                        dynamic[Infrastructure.Constants.Wire.NumberPrefix + pathHash] =
                            number.ToString("R", CultureInfo.InvariantCulture);
                    else if (IsNumeric(property.Value))
                        unordered.Add(pathHash);
                    break;
            }
        }
    }

    private static string Join(IEnumerable<string> values) => string.Join(
        Infrastructure.Constants.Wire.TagSeparatorCharacter,
        values.OrderBy(static value => value, StringComparer.Ordinal));

    private static string ValueToken(string pathHash, string canonical) =>
        Hex(pathHash + "\n" + canonical);

    private static string Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool TryCanonical(object? value, out string canonical)
    {
        switch (value)
        {
            case null:
                canonical = "null";
                return true;
            case string text:
                canonical = "string:" + text;
                return true;
            case bool boolean:
                canonical = boolean ? "bool:1" : "bool:0";
                return true;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                if (!TryDecimal(value, out var number))
                {
                    canonical = string.Empty;
                    return false;
                }
                canonical = "number:" + number.ToString("G29", CultureInfo.InvariantCulture);
                return true;
            case Guid guid:
                canonical = "guid:" + guid.ToString("D");
                return true;
            case DateOnly date:
                canonical = "date:" + date.DayNumber.ToString(CultureInfo.InvariantCulture);
                return true;
            case TimeOnly time:
                canonical = "time:" + time.Ticks.ToString(CultureInfo.InvariantCulture);
                return true;
            case DateTime date:
                canonical = "datetime:" + date.Ticks.ToString(CultureInfo.InvariantCulture);
                return true;
            case DateTimeOffset date:
                canonical = "datetimeoffset:" + date.UtcTicks.ToString(CultureInfo.InvariantCulture);
                return true;
            case TimeSpan duration:
                canonical = "timespan:" + duration.Ticks.ToString(CultureInfo.InvariantCulture);
                return true;
            case byte[] or DataObject or DataArray:
                canonical = string.Empty;
                return false;
            default:
                throw Unsupported(null, null, $"metadata value type '{value.GetType().FullName}'");
        }
    }

    private static bool TryDecimal(object value, out decimal number)
    {
        try
        {
            if (value is float floating && !float.IsFinite(floating) ||
                value is double precision && !double.IsFinite(precision) ||
                value is not (sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal))
            {
                number = default;
                return false;
            }
            number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (OverflowException)
        {
            number = default;
            return false;
        }
    }

    private static bool IsNumeric(object value) => value is
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool TryNumericOrder(object value, out double number)
    {
        if (value is bool boolean)
        {
            number = boolean ? 1d : 0d;
            return true;
        }
        if (!TryDecimal(value, out var exact))
        {
            number = default;
            return false;
        }
        number = (double)exact;
        return double.IsFinite(number) && (decimal)number == exact;
    }

    private static object? Scalar(FieldFilter filter) => filter.Value switch
    {
        FilterValue.Scalar scalar => scalar.Value,
        FilterValue.Set set when set.Values.Count > 0 => set.Values[0],
        _ => null
    };

    private static IReadOnlyList<object?> Set(FieldFilter filter) => filter.Value switch
    {
        FilterValue.Set set => set.Values,
        FilterValue.Scalar scalar => [scalar.Value],
        _ => []
    };

    private sealed class Compiler(IReadOnlySet<string> attributes)
    {
        internal RedisVectorCompiledFilter Write(Filter filter) => filter switch
        {
            AllOf all => Group(all.Operands, all: true),
            AnyOf any => Group(any.Operands, all: false),
            Not not => Negate(Write(not.Operand)),
            FieldFilter field => WriteField(field),
            _ => throw Unsupported(null, null, $"filter node '{filter.GetType().Name}'")
        };

        private RedisVectorCompiledFilter Group(IReadOnlyList<Filter> operands, bool all)
        {
            var values = operands.Select(Write).ToArray();
            if (all)
            {
                if (values.Any(static value => value.IsFalse)) return RedisVectorCompiledFilter.False;
                var active = values.Where(static value => !value.IsTrue).ToArray();
                return active.Length == 0
                    ? RedisVectorCompiledFilter.True
                    : active.Length == 1
                        ? active[0]
                        : new RedisVectorCompiledFilter("(" + string.Join(' ', active.Select(static value => value.Query)) + ")");
            }
            if (values.Any(static value => value.IsTrue)) return RedisVectorCompiledFilter.True;
            var choices = values.Where(static value => !value.IsFalse).ToArray();
            return choices.Length == 0
                ? RedisVectorCompiledFilter.False
                : choices.Length == 1
                    ? choices[0]
                    : new RedisVectorCompiledFilter("(" + string.Join(" | ", choices.Select(static value => value.Query)) + ")");
        }

        private static RedisVectorCompiledFilter Negate(RedisVectorCompiledFilter value) => value.IsTrue
            ? RedisVectorCompiledFilter.False
            : value.IsFalse
                ? RedisVectorCompiledFilter.True
                : new RedisVectorCompiledFilter("-(" + value.Query + ")");

        private RedisVectorCompiledFilter WriteField(FieldFilter field)
        {
            if (field.IgnoreCase)
                throw Unsupported(field.Operator, field.Field.ToString(), "case-insensitive comparison");
            var path = PathHash(field.Field.Segments);
            return field.Operator switch
            {
                FilterOperator.Eq => Equal(path, Scalar(field)),
                FilterOperator.Ne => Negate(Equal(path, Scalar(field))),
                FilterOperator.Gt => Range(path, "(" + Number(Scalar(field), field), "+inf"),
                FilterOperator.Gte => Range(path, Number(Scalar(field), field), "+inf"),
                FilterOperator.Lt => Range(path, "-inf", "(" + Number(Scalar(field), field)),
                FilterOperator.Lte => Range(path, "-inf", Number(Scalar(field), field)),
                FilterOperator.In => Any(path, Set(field), Infrastructure.Constants.Wire.Scalar),
                FilterOperator.Nin => Negate(Any(path, Set(field), Infrastructure.Constants.Wire.Scalar)),
                FilterOperator.Has => Match(Infrastructure.Constants.Wire.Elements,
                    ValueToken(path, Canonical(Scalar(field), field))),
                FilterOperator.HasAny => Any(path, Set(field), Infrastructure.Constants.Wire.Elements),
                FilterOperator.HasAll => All(path, Set(field), Infrastructure.Constants.Wire.Elements),
                FilterOperator.HasNone => Negate(Any(path, Set(field), Infrastructure.Constants.Wire.Elements)),
                FilterOperator.Size => Size(path, Scalar(field), field),
                FilterOperator.Exists => Exists(path, Scalar(field)),
                _ => throw Unsupported(field.Operator, field.Field.ToString(), "operator")
            };
        }

        private static RedisVectorCompiledFilter Equal(string path, object? value)
        {
            if (value is null) return Negate(Match(Infrastructure.Constants.Wire.Present, path));
            return Match(Infrastructure.Constants.Wire.Scalar, ValueToken(path, Canonical(value, null)));
        }

        private RedisVectorCompiledFilter Range(string path, string lower, string upper)
        {
            var name = Infrastructure.Constants.Wire.NumberPrefix + path;
            if (!attributes.Contains(name)) return RedisVectorCompiledFilter.False;
            return new RedisVectorCompiledFilter($"@{name}:[{lower} {upper}]");
        }

        private static string Number(object? value, FieldFilter field)
        {
            if (value is null || !TryNumericOrder(value, out var number))
                throw Unsupported(field.Operator, field.Field.ToString(),
                    value is null ? "null comparison" : $"comparison value type '{value.GetType().FullName}' or precision");
            return number.ToString("R", CultureInfo.InvariantCulture);
        }

        private static RedisVectorCompiledFilter Any(string path, IReadOnlyList<object?> values, string field)
        {
            if (values.Count == 0) return RedisVectorCompiledFilter.False;
            var matches = values.Select(value => value is null && field == Infrastructure.Constants.Wire.Scalar
                    ? Negate(Match(Infrastructure.Constants.Wire.Present, path))
                    : Match(field, ValueToken(path, Canonical(value, null))))
                .ToArray();
            if (matches.Any(static match => match.IsTrue)) return RedisVectorCompiledFilter.True;
            return matches.Length == 1
                ? matches[0]
                : new RedisVectorCompiledFilter("(" + string.Join(" | ", matches.Select(static match => match.Query)) + ")");
        }

        private static RedisVectorCompiledFilter All(string path, IReadOnlyList<object?> values, string field)
        {
            if (values.Count == 0) return RedisVectorCompiledFilter.True;
            var matches = values.Select(value => Match(field, ValueToken(path, Canonical(value, null)))).ToArray();
            return matches.Length == 1
                ? matches[0]
                : new RedisVectorCompiledFilter("(" + string.Join(' ', matches.Select(static match => match.Query)) + ")");
        }

        private RedisVectorCompiledFilter Size(string path, object? value, FieldFilter field)
        {
            decimal numeric = default;
            if (value is not null && !TryDecimal(value, out numeric))
                throw Unsupported(field.Operator, field.Field.ToString(), "non-integral size");
            int expected;
            try { expected = value is null ? 0 : (int)numeric; }
            catch (OverflowException) { throw Unsupported(field.Operator, field.Field.ToString(), "size outside Int32 range"); }
            if (expected < 0) return RedisVectorCompiledFilter.False;
            var name = Infrastructure.Constants.Wire.SizePrefix + path;
            if (!attributes.Contains(name))
                return expected == 0 ? RedisVectorCompiledFilter.True : RedisVectorCompiledFilter.False;
            return expected == 0
                ? new RedisVectorCompiledFilter($"-@{name}:[(0 +inf]")
                : new RedisVectorCompiledFilter($"@{name}:[{expected} {expected}]");
        }

        private static RedisVectorCompiledFilter Exists(string path, object? value)
        {
            var present = Match(Infrastructure.Constants.Wire.Present, path);
            return value is not bool expected || expected ? present : Negate(present);
        }

        private static RedisVectorCompiledFilter Match(string field, string token) =>
            new($"@{field}:{{{token}}}");

        private static string Canonical(object? value, FieldFilter? field)
        {
            if (TryCanonical(value, out var canonical)) return canonical;
            throw Unsupported(field?.Operator, field?.Field.ToString(),
                value is null ? "value" : $"reference-identity value type '{value.GetType().FullName}'");
        }
    }

    private static VectorFilterUnsupportedException Unsupported(
        FilterOperator? operation,
        string? field,
        string detail) =>
        new(Infrastructure.Constants.Provider.Name, operation, field,
            $"RedisVector cannot faithfully push {detail}. Narrow the filter or select an adapter that declares it.");
}

internal sealed record RedisVectorProjection(
    string Present,
    string Scalar,
    string Elements,
    string Unordered,
    IReadOnlyList<KeyValuePair<string, string>> Dynamic,
    IReadOnlyList<string> Paths)
{
    internal static RedisVectorProjection Empty { get; } =
        new(string.Empty, string.Empty, string.Empty, string.Empty, [], []);
}

internal sealed record RedisVectorCompiledFilter(string Query, bool IsTrue = false, bool IsFalse = false)
{
    internal static RedisVectorCompiledFilter True { get; } = new("*", IsTrue: true);
    internal static RedisVectorCompiledFilter False { get; } = new(string.Empty, IsFalse: true);
}
