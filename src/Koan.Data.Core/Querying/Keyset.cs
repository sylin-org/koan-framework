using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core.Sorting;

namespace Koan.Data.Core.Querying;

/// <summary>
/// A declared order a list continues through (DATA-0170): the members it names, then the Entity identity, which breaks every tie. A page
/// is read after a <see cref="KeysetKey"/>, the place of the last row the page before it held, never at a numbered position, so a row
/// present for the whole reading is read once whatever another writer inserts or removes ahead of it, each page costs the same however
/// deep it is, and nothing is counted.
/// <code>
/// static readonly Keyset&lt;Post&gt; Latest = Keyset&lt;Post&gt;.By(order => order.OrderByDescending(post => post.LastActivityAt));
/// var page = await Post.Page(Latest, after, 20, post => post.TangentId == tangent, ct); // page.Items, page.Next
/// </code>
/// <para>The seek compares in the store, through the same filter pushdown every query uses, and the page is ordered by the same store,
/// so both use the store's own comparison of each member: text by its collation, a date by its stored form. A provider that cannot push
/// the comparison, or apply the order itself, refuses the read rather than paging in memory. A member whose type can hold null narrows the
/// list to rows where it is set, because stores place nulls differently and a null has no place to continue from. The identity follows the
/// direction of the last member named, or ascends where none is named.</para>
/// <para>The application decides its own loop: a reader that judges rows itself reads <see cref="KeysetPage{TEntity}.Next"/> again until its
/// page is full.</para>
/// </summary>
public sealed class Keyset<TEntity> where TEntity : class
{
    private static readonly JsonSerializerOptions KeyJson = new(JsonSerializerDefaults.General);

    private readonly FieldPath[] _fields;
    private readonly MemberInfo[][] _members;
    private readonly Filter? _set;

    private Keyset(IReadOnlyList<SortSpec> named)
    {
        var where = typeof(TEntity).Name;
        if (AggregateMetadata.GetIdSpec(typeof(TEntity))?.Prop is not { } identity)
            throw new InvalidOperationException($"A keyset order over {where} needs an Entity identity to break its ties.");
        if (!QueryStreamCoordinator.IsProviderStableIdentifierType(identity.PropertyType))
            throw new InvalidOperationException($"A keyset order over {where} breaks its ties by the identity, and an identity of type {identity.PropertyType.Name} has no order every store applies alike. Use a string, Guid, int or long identity.");

        var order = new List<SortSpec>();
        foreach (var spec in named)
        {
            if (IsIdentity(spec.Path, identity))
            {
                order.Add(spec);
                break;
            }
            if (spec.Path.TraversesCollection || spec.Aggregation != SortAggregation.None)
                throw new InvalidOperationException($"A keyset order over {where} names '{spec.Path.DotPath}', which holds many values; name one value.");
            if (!IsScalar(spec.Path.ValueType))
                throw new InvalidOperationException($"A keyset order over {where} names '{spec.Path.DotPath}', a {spec.Path.ValueType.Name}, which no store compares as one value; name a value inside it.");
            order.Add(spec);
        }
        if (order.Count == 0 || !IsIdentity(order[^1].Path, identity))
            order.Add(new SortSpec(new MemberPath(typeof(TEntity), [identity], identity.PropertyType, traversesCollection: false, collectionSegmentIndex: -1),
                Desc: order.Count > 0 && order[^1].Desc));

        Order = order;
        _fields = order.Select(spec => FieldPath.Of(spec.Path.Members.Select(member => member.Name).ToArray())).ToArray();
        _members = order.Select(spec => spec.Path.Members.ToArray()).ToArray();
        // A member that can hold null keeps the list to rows where it is set; the identity is always set.
        _set = order.Take(order.Count - 1).Where(spec => CanBeNull(spec.Path.ValueType))
            .Select(spec => Filter.On(FieldPath.Of(spec.Path.Members.Select(member => member.Name).ToArray()), FilterOperator.Ne, FilterValue.Of(null)))
            .Aggregate((Filter?)null, Filter.And);
    }

    /// <summary>The order a list continues through: <paramref name="order"/>'s members, then the identity.</summary>
    public static Keyset<TEntity> By(Action<ISortBuilder<TEntity>> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(SortBuilder<TEntity>.Build(order));
    }

    /// <summary>The order a list continues through, in the sort grammar (<c>"-LastActivityAt"</c>, <c>"Order"</c>), then the identity; the
    /// identity alone where <paramref name="order"/> names nothing.</summary>
    public static Keyset<TEntity> By(string? order) => new(SortSpecParser.ParseStrict<TEntity>(order));

    /// <summary>The order as the store applies it: the members named, then the identity.</summary>
    public IReadOnlyList<SortSpec> Order { get; }

    /// <summary>The place of <paramref name="row"/> in this order, which the page after it continues from.</summary>
    public KeysetKey KeyOf(TEntity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var values = new object?[_members.Length];
        for (var index = 0; index < values.Length; index++)
        {
            object? value = row;
            foreach (var member in _members[index])
                value = value is null ? null : member switch
                {
                    PropertyInfo property => property.GetValue(value),
                    FieldInfo field => field.GetValue(value),
                    _ => null,
                };
            values[index] = value ?? throw new InvalidOperationException(
                $"A {typeof(TEntity).Name} in a keyset order holds no '{Order[index].Path.DotPath}', so no page can continue after it.");
        }
        return new KeysetKey(values);
    }

    /// <summary>The key as text, typed by this order's members, so <see cref="Read"/> answers it exactly.</summary>
    public string Write(KeysetKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Values.Count != Order.Count) throw new ArgumentException("The key is not a key of this order.", nameof(key));
        var array = new JsonArray();
        for (var index = 0; index < Order.Count; index++)
            array.Add(JsonSerializer.SerializeToNode(key.Values[index], Order[index].Path.ValueType, KeyJson));
        return array.ToJsonString(KeyJson);
    }

    /// <summary>The key <paramref name="text"/> holds, or null where it is not a key of this order.</summary>
    public KeysetKey? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            if (JsonNode.Parse(text) is not JsonArray array || array.Count != Order.Count) return null;
            var values = new object?[Order.Count];
            for (var index = 0; index < values.Length; index++)
                if ((values[index] = array[index]?.Deserialize(Order[index].Path.ValueType, KeyJson)) is null) return null;
            return new KeysetKey(values);
        }
        catch (Exception error) when (error is JsonException or FormatException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The rows strictly after <paramref name="key"/> in this order, in the form an index on the order answers as one range: a
    /// member no further than the key's and, at the key's own value, the rest of the order after the key's.</summary>
    internal Filter After(KeysetKey key)
    {
        if (key.Values.Count != Order.Count) throw new ArgumentException("The key is not a key of this order.", nameof(key));
        Filter? seek = null;
        for (var index = Order.Count - 1; index >= 0; index--)
        {
            var value = FilterValue.Of(key.Values[index]);
            var strict = Filter.On(_fields[index], Order[index].Desc ? FilterOperator.Lt : FilterOperator.Gt, value);
            seek = seek is null
                ? strict
                : Filter.All(Filter.On(_fields[index], Order[index].Desc ? FilterOperator.Lte : FilterOperator.Gte, value), Filter.Any(strict, seek));
        }
        return seek!;
    }

    /// <summary>The read of the page after <paramref name="after"/>, or the first page: <paramref name="query"/>'s own filter, the rows where
    /// each member is set, the seek, this order, and <paramref name="take"/> rows.</summary>
    internal QueryDefinition Continue(QueryDefinition query, KeysetKey? after, int take)
        => query
            .Where(Filter.And(Filter.And(query.Filter, _set), after is null ? null : After(after)))
            .WithSort(Order.ToArray())
            .WithPagination(1, take)
            .WithCountStrategy(null);

    /// <summary>Whether a provider of <paramref name="support"/> compares the whole seek in its store.</summary>
    internal bool Pushes(FilterSupport support)
    {
        var probe = After(new KeysetKey(new object?[Order.Count]));
        return FilterSplitter.Split(_set is null ? probe : Filter.All(_set, probe), support, typeof(TEntity)).Residual is null;
    }

    /// <summary>The order of a read that leads with the identity, which needs nothing after it, or null where the read leads with another member.</summary>
    internal static Keyset<TEntity>? IdentityFirst(IReadOnlyList<SortSpec> sort, Func<MemberPath, bool> isIdentity)
        => sort.Count > 0 && isIdentity(sort[0].Path) && AggregateMetadata.GetIdSpec(typeof(TEntity))?.Prop is { } identity
            ? new([new SortSpec(new MemberPath(typeof(TEntity), [identity], identity.PropertyType, traversesCollection: false, collectionSegmentIndex: -1), sort[0].Desc)])
            : null;

    private static bool IsIdentity(MemberPath path, PropertyInfo identity)
        => path.Members.Count == 1 && string.Equals(path.Members[0].Name, identity.Name, StringComparison.Ordinal);

    private static bool IsScalar(Type type) => type == typeof(string) || (Nullable.GetUnderlyingType(type) ?? type).IsValueType;

    private static bool CanBeNull(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
}
