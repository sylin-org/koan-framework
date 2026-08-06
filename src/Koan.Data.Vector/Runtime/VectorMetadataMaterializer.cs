using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Koan.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector;

internal sealed class VectorMetadataMaterializer
{
    private const string ReservedPrefix = "__koan_";
    private readonly object _gate = new();
    private readonly Dictionary<Type, Accessor[]> _plans = new();
    private readonly int _capacity;
    private readonly int _maxDepth;

    public VectorMetadataMaterializer(IOptions<VectorDefaultsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _capacity = Positive(options.Value.MetadataShapeEntries, nameof(VectorDefaultsOptions.MetadataShapeEntries));
        _maxDepth = Positive(options.Value.MaxMetadataDepth, nameof(VectorDefaultsOptions.MaxMetadataDepth));
    }

    public DataObject? Materialize(object? metadata)
    {
        if (metadata is null) return null;
        var value = Normalize(metadata, 0);
        return value as DataObject ?? throw new InvalidOperationException(
            $"Vector metadata must be an object, dictionary, or POCO; received '{metadata.GetType().FullName}'.");
    }

    private object? Normalize(object? value, int depth)
    {
        if (depth > _maxDepth)
            throw new InvalidOperationException($"Vector metadata exceeds the configured depth of {_maxDepth}.");
        return value switch
        {
            null => null,
            DataObject data => new DataObject(data.Properties.Select(property =>
                Property(property.Name, Normalize(property.Value, depth + 1)))),
            DataArray array => new DataArray(array.Items.Select(item => Normalize(item, depth + 1))),
            JsonElement json => NormalizeJson(json, depth),
            string or bool or sbyte or byte or short or ushort or int or uint or long or ulong or
                float or double or decimal or Guid or DateOnly or TimeOnly or DateTime or DateTimeOffset or
                TimeSpan => value,
            byte[] bytes => bytes.ToArray(),
            Enum enumeration => Convert.ChangeType(enumeration, Enum.GetUnderlyingType(enumeration.GetType()), System.Globalization.CultureInfo.InvariantCulture),
            IDictionary dictionary => NormalizeDictionary(dictionary, depth + 1),
            IEnumerable sequence => new DataArray(sequence.Cast<object?>().Select(item => Normalize(item, depth + 1))),
            _ => NormalizeObject(value, depth + 1)
        };
    }

    private DataObject NormalizeObject(object value, int depth)
    {
        var accessors = Plan(value.GetType());
        return new DataObject(accessors.Select(accessor =>
            Property(accessor.Name, Normalize(accessor.Get(value), depth))));
    }

    private DataObject NormalizeDictionary(IDictionary dictionary, int depth)
    {
        var properties = new List<DataProperty>(dictionary.Count);
        foreach (DictionaryEntry item in dictionary)
        {
            if (item.Key is not string name)
                throw new InvalidOperationException("Vector metadata dictionary keys must be strings.");
            properties.Add(Property(name, Normalize(item.Value, depth)));
        }
        return new DataObject(properties);
    }

    private object? NormalizeJson(JsonElement json, int depth) => json.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => json.TryGetDateTimeOffset(out var date) ? date : json.GetString(),
        JsonValueKind.Number => json.TryGetInt64(out var integer) ? integer : json.GetDouble(),
        JsonValueKind.Array => new DataArray(json.EnumerateArray().Select(item => NormalizeJson(item, depth + 1))),
        JsonValueKind.Object => new DataObject(json.EnumerateObject().Select(item =>
            Property(item.Name, NormalizeJson(item.Value, depth + 1)))),
        _ => throw new InvalidOperationException($"JSON metadata kind '{json.ValueKind}' is unsupported.")
    };

    private Accessor[] Plan(Type type)
    {
        lock (_gate)
        {
            if (_plans.TryGetValue(type, out var existing)) return existing;
            if (_plans.Count >= _capacity)
                throw new InvalidOperationException(
                    $"The host Vector metadata-shape cache reached its configured limit of {_capacity}. " +
                    "Reduce metadata POCO shapes or increase VectorDefaults:MetadataShapeEntries.");
            var created = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
                .OrderBy(static property => property.MetadataToken)
                .Select(CreateAccessor)
                .ToArray();
            if (created.Length == 0)
                throw new InvalidOperationException(
                    $"Vector metadata type '{type.FullName}' has no readable public properties.");
            _plans.Add(type, created);
            return created;
        }
    }

    private static Accessor CreateAccessor(PropertyInfo property)
    {
        var value = Expression.Parameter(typeof(object), "value");
        var read = Expression.Property(Expression.Convert(value, property.DeclaringType!), property);
        var box = Expression.Convert(read, typeof(object));
        return new Accessor(property.Name, Expression.Lambda<Func<object, object?>>(box, value).Compile());
    }

    private static DataProperty Property(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.StartsWith(ReservedPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Vector metadata property '{name}' uses the reserved '{ReservedPrefix}' framework namespace.");
        return new DataProperty(name, value);
    }

    private static int Positive(int value, string name) =>
        value > 0 ? value : throw new InvalidOperationException($"{name} must be greater than zero.");

    private sealed record Accessor(string Name, Func<object, object?> Get);
}
