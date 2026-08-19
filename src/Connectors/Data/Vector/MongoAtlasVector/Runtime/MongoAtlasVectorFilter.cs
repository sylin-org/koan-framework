using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using MongoDB.Bson;

namespace Koan.Data.Vector.Connector.MongoAtlasVector;

/// <summary>
/// Lowers the neutral metadata algebra into a stable, field-name-safe Atlas Search projection.
/// The same tokens drive native vector-search filters and ordinary Mongo predicates used by
/// scoped reads/deletes, so the two paths cannot drift semantically.
/// </summary>
internal static class MongoAtlasVectorFilter
{
    internal static readonly FilterSupport Capabilities = FilterSupport.Uniform(
        nestedPaths: true,
        ignoreCase: false,
        FilterOperator.Eq, FilterOperator.Ne,
        FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
        FilterOperator.In, FilterOperator.Nin,
        FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll, FilterOperator.HasNone,
        FilterOperator.Size, FilterOperator.Exists);

    internal static Projection Project(DataObject? metadata)
    {
        var scalar = new BsonArray();
        var elements = new BsonArray();
        var present = new BsonArray();
        var numeric = new BsonDocument();
        var size = new BsonDocument();
        if (metadata is not null)
            Walk(metadata, [], scalar, elements, present, numeric, size);
        return new Projection(scalar, elements, present, numeric, size);
    }

    internal static BsonDocument CompileSearch(Filter? filter) => filter is null
        ? TrueSearch()
        : Search(filter);

    internal static BsonDocument CompileMatch(Filter? filter) => filter is null
        ? new BsonDocument()
        : Match(filter);

    internal static BsonDocument SearchText(string field, string token) => new("text", new BsonDocument
    {
        ["path"] = field,
        ["query"] = token
    });

    internal static string Path(FieldPath path)
    {
        Validate(path);
        return Path(path.Segments);
    }

    internal static string ValueToken(FieldPath field, object? value) => Token(Path(field), value);

    private static void Walk(
        DataObject data,
        IReadOnlyList<string> parent,
        BsonArray scalar,
        BsonArray elements,
        BsonArray present,
        BsonDocument numeric,
        BsonDocument size)
    {
        foreach (var property in data.Properties)
        {
            if (string.IsNullOrWhiteSpace(property.Name))
                throw Unsupported(null, property.Name, "an empty metadata path segment");
            var segments = new string[parent.Count + 1];
            for (var index = 0; index < parent.Count; index++) segments[index] = parent[index];
            segments[^1] = property.Name;
            ProjectValue(property.Value, segments, scalar, elements, present, numeric, size);
        }
    }

    private static void ProjectValue(
        object? value,
        IReadOnlyList<string> path,
        BsonArray scalar,
        BsonArray elements,
        BsonArray present,
        BsonDocument numeric,
        BsonDocument size)
    {
        if (value is null) return;
        var key = Path(path);
        present.Add(key);
        switch (value)
        {
            case DataArray array:
                size[key] = array.Items.Count;
                scalar.Add(SizeToken(key, array.Items.Count));
                foreach (var item in array.Items)
                    elements.Add(Token(key, item));
                return;
            case DataObject child:
                size[key] = 0;
                scalar.Add(SizeToken(key, 0));
                Walk(child, path, scalar, elements, present, numeric, size);
                return;
            default:
                scalar.Add(Token(key, value));
                scalar.Add(SizeToken(key, 0));
                size[key] = 0;
                if (TryNumber(value, out var number)) numeric[key] = number;
                return;
        }
    }

    private static BsonDocument Search(Filter filter) => filter switch
    {
        AllOf all => SearchAll(all.Operands),
        AnyOf any => SearchAny(any.Operands),
        Not not => SearchNot(Search(not.Operand)),
        FieldFilter field => SearchField(field),
        _ => throw Unsupported(null, null, $"filter node '{filter.GetType().Name}'")
    };

    private static BsonDocument SearchAll(IReadOnlyList<Filter> operands)
    {
        if (operands.Count == 0) return TrueSearch();
        if (operands.Count == 1) return Search(operands[0]);
        return new BsonDocument("compound", new BsonDocument("filter",
            new BsonArray(operands.Select(Search))));
    }

    private static BsonDocument SearchAny(IReadOnlyList<Filter> operands)
    {
        if (operands.Count == 0) return FalseSearch();
        if (operands.Count == 1) return Search(operands[0]);
        return new BsonDocument("compound", new BsonDocument
        {
            ["should"] = new BsonArray(operands.Select(Search)),
            ["minimumShouldMatch"] = 1
        });
    }

    private static BsonDocument SearchNot(BsonDocument operand) =>
        new("compound", new BsonDocument("mustNot", new BsonArray { operand }));

    private static BsonDocument SearchField(FieldFilter field)
    {
        RejectIgnoreCase(field);
        var key = Path(field.Field);
        var scalar = Scalar(field.Value);
        var set = Set(field.Value);
        return field.Operator switch
        {
            FilterOperator.Eq => scalar is null
                ? SearchNot(SearchText(Infrastructure.Constants.Wire.Present, key))
                : SearchText(Infrastructure.Constants.Wire.Scalar, Token(key, scalar)),
            FilterOperator.Ne => SearchNot(scalar is null
                ? SearchNot(SearchText(Infrastructure.Constants.Wire.Present, key))
                : SearchText(Infrastructure.Constants.Wire.Scalar, Token(key, scalar))),
            FilterOperator.In => SearchIn(key, set, Infrastructure.Constants.Wire.Scalar),
            FilterOperator.Nin => SearchNot(SearchIn(key, set, Infrastructure.Constants.Wire.Scalar)),
            FilterOperator.Has => SearchText(Infrastructure.Constants.Wire.Elements, Token(key, scalar)),
            FilterOperator.HasAny => SearchSet(key, set, Infrastructure.Constants.Wire.Elements, all: false),
            FilterOperator.HasAll => SearchSet(key, set, Infrastructure.Constants.Wire.Elements, all: true),
            FilterOperator.HasNone => SearchNot(SearchSet(key, set, Infrastructure.Constants.Wire.Elements, all: false)),
            FilterOperator.Size => SearchSize(key, scalar, field),
            FilterOperator.Exists => DesiredExists(scalar)
                ? SearchText(Infrastructure.Constants.Wire.Present, key)
                : SearchNot(SearchText(Infrastructure.Constants.Wire.Present, key)),
            FilterOperator.Gt or FilterOperator.Gte or FilterOperator.Lt or FilterOperator.Lte =>
                SearchRange(key, field.Operator, scalar, field),
            _ => throw Unsupported(field.Operator, field.Field.ToString(), "the requested operator")
        };
    }

    private static BsonDocument SearchIn(string key, IReadOnlyList<object?> values, string field)
    {
        if (values.Count == 0) return FalseSearch();
        var clauses = new List<BsonDocument>(values.Count);
        foreach (var value in values)
            clauses.Add(value is null
                ? SearchNot(SearchText(Infrastructure.Constants.Wire.Present, key))
                : SearchText(field, Token(key, value)));
        return clauses.Count == 1 ? clauses[0] : SearchAnyDocuments(clauses);
    }

    private static BsonDocument SearchSet(
        string key,
        IReadOnlyList<object?> values,
        string field,
        bool all)
    {
        if (values.Count == 0) return all ? TrueSearch() : FalseSearch();
        var clauses = values.Select(value => SearchText(field, Token(key, value))).ToArray();
        return all ? SearchAllDocuments(clauses) : SearchAnyDocuments(clauses);
    }

    private static BsonDocument SearchAllDocuments(IReadOnlyList<BsonDocument> clauses) =>
        clauses.Count == 1 ? clauses[0] : new BsonDocument("compound",
            new BsonDocument("filter", new BsonArray(clauses)));

    private static BsonDocument SearchAnyDocuments(IReadOnlyList<BsonDocument> clauses) =>
        clauses.Count == 1 ? clauses[0] : new BsonDocument("compound", new BsonDocument
        {
            ["should"] = new BsonArray(clauses),
            ["minimumShouldMatch"] = 1
        });

    private static BsonDocument SearchSize(string key, object? value, FieldFilter field)
    {
        var count = Count(value);
        if (count < 0) return FalseSearch();
        var exact = SearchText(Infrastructure.Constants.Wire.Scalar, SizeToken(key, count));
        return count == 0
            ? SearchAnyDocuments([exact, SearchNot(SearchText(Infrastructure.Constants.Wire.Present, key))])
            : exact;
    }

    private static BsonDocument SearchRange(
        string key,
        FilterOperator operation,
        object? value,
        FieldFilter field)
    {
        if (value is null) return FalseSearch();
        if (!TryNumber(value, out var number))
            throw Unsupported(operation, field.Field.ToString(),
                $"ordered comparison value type '{value.GetType().FullName}'");
        var name = operation switch
        {
            FilterOperator.Gt => "gt",
            FilterOperator.Gte => "gte",
            FilterOperator.Lt => "lt",
            FilterOperator.Lte => "lte",
            _ => throw new UnreachableException()
        };
        return new BsonDocument("range", new BsonDocument
        {
            ["path"] = Infrastructure.Constants.Wire.Numeric + "." + key,
            [name] = number
        });
    }

    private static BsonDocument TrueSearch() => new("exists",
        new BsonDocument("path", Infrastructure.Constants.Wire.Generation));

    private static BsonDocument FalseSearch() => SearchText(
        Infrastructure.Constants.Wire.Generation,
        "__koan_never_matches__");

    private static BsonDocument Match(Filter filter) => filter switch
    {
        AllOf all => MatchAll(all.Operands),
        AnyOf any => MatchAny(any.Operands),
        Not not => NotMatch(Match(not.Operand)),
        FieldFilter field => MatchField(field),
        _ => throw Unsupported(null, null, $"filter node '{filter.GetType().Name}'")
    };

    private static BsonDocument MatchAll(IReadOnlyList<Filter> operands)
    {
        if (operands.Count == 0) return new BsonDocument();
        if (operands.Count == 1) return Match(operands[0]);
        return new BsonDocument("$and", new BsonArray(operands.Select(Match)));
    }

    private static BsonDocument MatchAny(IReadOnlyList<Filter> operands)
    {
        if (operands.Count == 0) return FalseMatch();
        if (operands.Count == 1) return Match(operands[0]);
        return new BsonDocument("$or", new BsonArray(operands.Select(Match)));
    }

    private static BsonDocument NotMatch(BsonDocument value) =>
        new("$nor", new BsonArray { value });

    private static BsonDocument MatchField(FieldFilter field)
    {
        RejectIgnoreCase(field);
        var key = Path(field.Field);
        var scalar = Scalar(field.Value);
        var set = Set(field.Value);
        var presentPath = Infrastructure.Constants.Wire.Present;
        return field.Operator switch
        {
            FilterOperator.Eq => scalar is null
                ? NotMatch(TokenMatch(presentPath, key))
                : TokenMatch(Infrastructure.Constants.Wire.Scalar, Token(key, scalar)),
            FilterOperator.Ne => NotMatch(scalar is null
                ? NotMatch(TokenMatch(presentPath, key))
                : TokenMatch(Infrastructure.Constants.Wire.Scalar, Token(key, scalar))),
            FilterOperator.In => MatchIn(key, set, Infrastructure.Constants.Wire.Scalar),
            FilterOperator.Nin => NotMatch(MatchIn(key, set, Infrastructure.Constants.Wire.Scalar)),
            FilterOperator.Has => TokenMatch(Infrastructure.Constants.Wire.Elements, Token(key, scalar)),
            FilterOperator.HasAny => MatchSet(key, set, Infrastructure.Constants.Wire.Elements, all: false),
            FilterOperator.HasAll => MatchSet(key, set, Infrastructure.Constants.Wire.Elements, all: true),
            FilterOperator.HasNone => NotMatch(MatchSet(key, set, Infrastructure.Constants.Wire.Elements, all: false)),
            FilterOperator.Size => MatchSize(key, scalar),
            FilterOperator.Exists => DesiredExists(scalar)
                ? TokenMatch(presentPath, key)
                : NotMatch(TokenMatch(presentPath, key)),
            FilterOperator.Gt or FilterOperator.Gte or FilterOperator.Lt or FilterOperator.Lte =>
                MatchRange(key, field.Operator, scalar, field),
            _ => throw Unsupported(field.Operator, field.Field.ToString(), "the requested operator")
        };
    }

    private static BsonDocument MatchIn(string key, IReadOnlyList<object?> values, string field)
    {
        if (values.Count == 0) return FalseMatch();
        var clauses = values.Select(value => value is null
            ? NotMatch(TokenMatch(Infrastructure.Constants.Wire.Present, key))
            : TokenMatch(field, Token(key, value))).ToArray();
        return clauses.Length == 1 ? clauses[0] : new BsonDocument("$or", new BsonArray(clauses));
    }

    private static BsonDocument MatchSet(
        string key,
        IReadOnlyList<object?> values,
        string field,
        bool all)
    {
        if (values.Count == 0) return all ? new BsonDocument() : FalseMatch();
        var clauses = values.Select(value => TokenMatch(field, Token(key, value))).ToArray();
        if (clauses.Length == 1) return clauses[0];
        return new BsonDocument(all ? "$and" : "$or", new BsonArray(clauses));
    }

    private static BsonDocument MatchSize(string key, object? value)
    {
        var count = Count(value);
        if (count < 0) return FalseMatch();
        var exact = TokenMatch(Infrastructure.Constants.Wire.Scalar, SizeToken(key, count));
        return count == 0
            ? new BsonDocument("$or", new BsonArray
            {
                exact,
                NotMatch(TokenMatch(Infrastructure.Constants.Wire.Present, key))
            })
            : exact;
    }

    private static BsonDocument MatchRange(
        string key,
        FilterOperator operation,
        object? value,
        FieldFilter field)
    {
        if (value is null) return FalseMatch();
        if (!TryNumber(value, out var number))
            throw Unsupported(operation, field.Field.ToString(),
                $"ordered comparison value type '{value.GetType().FullName}'");
        var name = operation switch
        {
            FilterOperator.Gt => "$gt",
            FilterOperator.Gte => "$gte",
            FilterOperator.Lt => "$lt",
            FilterOperator.Lte => "$lte",
            _ => throw new UnreachableException()
        };
        return new BsonDocument
        {
            [Infrastructure.Constants.Wire.Numeric + "." + key] = new BsonDocument
            {
                [name] = number
            }
        };
    }

    private static BsonDocument TokenMatch(string field, string token) => new(field, token);
    private static BsonDocument FalseMatch() => new("_id", new BsonDocument("$exists", false));

    private static void RejectIgnoreCase(FieldFilter field)
    {
        if (field.IgnoreCase)
            throw Unsupported(field.Operator, field.Field.ToString(), "case-insensitive comparison");
    }

    private static bool DesiredExists(object? value) => value is not bool desired || desired;

    private static int Count(object? value)
    {
        if (value is null) return 0;
        if (!TryDecimal(value, out var number)) return 0;
        try { return (int)number; }
        catch (OverflowException) { return int.MinValue; }
    }

    private static object? Scalar(FilterValue value) => value switch
    {
        FilterValue.Scalar scalar => scalar.Value,
        FilterValue.Set set when set.Values.Count > 0 => set.Values[0],
        _ => null
    };

    private static IReadOnlyList<object?> Set(FilterValue value) => value switch
    {
        FilterValue.Set set => set.Values,
        FilterValue.Scalar scalar => [scalar.Value],
        _ => []
    };

    private static string Path(IReadOnlyList<string> segments)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\u001f', segments));
        return "f_" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static void Validate(FieldPath path)
    {
        if (path.Segments.Count == 0 || path.Segments.Any(string.IsNullOrWhiteSpace))
            throw Unsupported(null, path.ToString(), "an empty metadata path segment");
    }

    private static string SizeToken(string path, int size) =>
        HashedToken(path, "size", size.ToString(CultureInfo.InvariantCulture));

    private static string Token(string path, object? value) =>
        HashedToken(path, "value", Canonical(value));

    private static string HashedToken(string path, string kind, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(path + "\u001e" + kind + "\u001e" + value);
        return "t_" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string Canonical(object? value) => value switch
    {
        null => "z",
        string text => "s:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
        bool boolean => boolean ? "b:1" : "b:0",
        Guid guid => "g:" + guid.ToString("D"),
        DateOnly date => "d:" + date.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly time => "t:" + time.ToString("O", CultureInfo.InvariantCulture),
        DateTime date => "dt:" + date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => "dto:" + date.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan duration => "ts:" + duration.ToString("c", CultureInfo.InvariantCulture),
        byte[] bytes => "x:" + Convert.ToBase64String(bytes),
        DataObject or DataArray => throw Unsupported(null, null, "object/array equality value"),
        _ when TryDecimal(value, out var number) => "n:" + number.ToString("G29", CultureInfo.InvariantCulture),
        _ => throw Unsupported(null, null, $"metadata value type '{value.GetType().FullName}'")
    };

    private static bool TryNumber(object? value, out BsonValue number)
    {
        if (!TryDecimal(value, out var decimalValue))
        {
            number = BsonNull.Value;
            return false;
        }
        var asDouble = (double)decimalValue;
        if (!double.IsFinite(asDouble))
        {
            number = BsonNull.Value;
            return false;
        }
        number = new BsonDouble(asDouble);
        return true;
    }

    private static bool TryDecimal(object? value, out decimal number)
    {
        try
        {
            switch (value)
            {
                case sbyte or byte or short or ushort or int or uint or long or ulong:
                    number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    return true;
                case float single when float.IsFinite(single):
                    number = (decimal)single;
                    return true;
                case double @double when double.IsFinite(@double):
                    number = (decimal)@double;
                    return true;
                case decimal valueDecimal:
                    number = valueDecimal;
                    return true;
                default:
                    number = default;
                    return false;
            }
        }
        catch (OverflowException)
        {
            number = default;
            return false;
        }
    }

    private static VectorFilterUnsupportedException Unsupported(
        FilterOperator? operation,
        string? field,
        string detail) => new(
            Infrastructure.Constants.Provider.Name,
            operation,
            field,
            $"MongoAtlasVector cannot faithfully push {detail}. Narrow the filter or select an adapter that declares it.");

    internal sealed record Projection(
        BsonArray Scalar,
        BsonArray Elements,
        BsonArray Present,
        BsonDocument Numeric,
        BsonDocument Size);
}
