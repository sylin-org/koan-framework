using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Semantics;
using Koan.Data.Relational.Mapping;

namespace Koan.Data.Relational;

/// <summary>
/// What one entity looks like in a relational store: the table it lives in, the columns a read selects, how a
/// row becomes an instance, and how a value becomes a parameter.
///
/// <para>This is the same work in every relational store, and it used to be written out once per adapter. Four
/// copies drift: the same fix lands in one and not the others, and the difference is invisible because each
/// copy reads correctly on its own. The four this replaced had already diverged on the comparer used to cache
/// decoded roots, for no reason and with no test that could see it.</para>
///
/// <para>A store differs from its siblings in three ways, and each is a seam here rather than a reason to copy
/// the file: what qualifies the table name (a schema, a database, or nothing), how a root appears in a SELECT
/// (<see cref="Project"/>), and whether a scalar is encoded on its way into a parameter
/// (<see cref="EncodeScalar"/>). What a store needs beyond those it adds as its own member; it does not
/// override behaviour it shares with the others.</para>
/// </summary>
/// <typeparam name="TEntity">The entity this plan maps.</typeparam>
/// <typeparam name="TKey">That entity's identity type.</typeparam>
/// <typeparam name="TDialect">The store's dialect, held concretely so an adapter reaches its own members
/// without a cast.</typeparam>
public abstract class RelationalEntityPlan<TEntity, TKey, TDialect>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
    where TDialect : IRelationalMappingDialect
{
    private readonly RelationalStructuredValueCodec _structured;
    private readonly MappingBindingPlan[] _readBindings;
    private readonly Dictionary<string, bool> _structuredRoots;
    private readonly Action<TEntity, object?>? _generatedSetter;
    private readonly MappingBindingPlan? _generatedBinding;
    private string? _select;

    /// <param name="mapping">The compiled map from entity to table.</param>
    /// <param name="segmentation">Which fields the framework manages rather than the application.</param>
    /// <param name="dialect">The store's dialect.</param>
    /// <param name="qualifier">What precedes the table name — a schema, a database, or <see langword="null"/>
    /// for a store with no such concept.</param>
    protected RelationalEntityPlan(
        MappingPlan mapping,
        DataSegmentationPlan segmentation,
        TDialect dialect,
        string? qualifier)
    {
        Mapping = mapping;
        Commands = new RelationalCommandPlanner(mapping);
        Dialect = dialect;
        Table = mapping.Container.Name;
        QualifiedTable = qualifier is null
            ? dialect.QuoteIdent(Table)
            : $"{dialect.QuoteIdent(qualifier)}.{dialect.QuoteIdent(Table)}";
        Target = qualifier is null
            ? $"{Table}/{mapping.Id}"
            : $"{qualifier}/{Table}/{mapping.Id}";
        _readBindings = mapping.Read().Bindings.ToArray();

        // Keyed by Koan's own physical names, so an exact comparison is the correct one. The row dictionary an
        // adapter hands to Hydrate is a different thing and is deliberately case-insensitive, because the names
        // in it came back from the server.
        _structuredRoots = mapping.Bindings
            .GroupBy(static binding => binding.PhysicalPath.Name, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Any(binding =>
                    binding.Shape == MappingValueShape.Object || binding.PhysicalPath.IsNested),
                StringComparer.Ordinal);
        _structured = new RelationalStructuredValueCodec(segmentation.For(typeof(TEntity)).Fields);
        Roots = _readBindings.Select(static binding => binding.PhysicalPath.Name)
            .Distinct(StringComparer.Ordinal).ToArray();
        IdentityRoots = mapping.Identity.Parts.Select(static part => part.PhysicalPath.Name).ToArray();

        if (mapping.Identity.IsGenerated && mapping.Identity.Parts.Count == 1 &&
            mapping.Identity.LogicalPath.Segments.Count == 1)
        {
            _generatedBinding = mapping.Bindings.Single(binding => binding.Id == mapping.Identity.Parts[0].Id);
            var property = typeof(TEntity).GetProperty(mapping.Identity.LogicalPath.Segments[0])
                ?? throw new MappingValueException(mapping.Id, mapping.Identity.LogicalPath.ToString(),
                    "Generated identity requires a writable Entity property.");
            var setter = property.SetMethod
                ?? throw new MappingValueException(mapping.Id, mapping.Identity.LogicalPath.ToString(),
                    "Generated identity requires a writable Entity property.");
            var entity = Expression.Parameter(typeof(TEntity), "entity");
            var value = Expression.Parameter(typeof(object), "value");
            _generatedSetter = Expression.Lambda<Action<TEntity, object?>>(
                Expression.Call(entity, setter, Expression.Convert(value, property.PropertyType)), entity, value).Compile();
        }
    }

    public MappingPlan Mapping { get; }
    public RelationalCommandPlanner Commands { get; }
    public TDialect Dialect { get; }
    public string Table { get; }
    public string QualifiedTable { get; }

    /// <summary>
    /// How this table names itself in readiness reporting and diagnostics: qualifier, table, and map. A store
    /// without a qualifier simply omits that leg, which is why this is stated once here rather than assembled
    /// from a differently-named property in each adapter.
    /// </summary>
    public string Target { get; }

    /// <summary>Every distinct column a read touches, in declaration order.</summary>
    public IReadOnlyList<string> Roots { get; }

    public IReadOnlyList<string> IdentityRoots { get; }

    /// <summary>
    /// The SELECT list. Built on first use rather than in the constructor, so <see cref="Project"/> is never
    /// called on a half-constructed derived instance.
    /// </summary>
    public string Select => _select ??= string.Join(", ", Roots.Select(Project));

    /// <summary>How one column appears in a SELECT. The default names it; a store overrides to cast or alias it.</summary>
    protected virtual string Project(string root) => Dialect.QuoteIdent(root);

    /// <summary>
    /// A scalar on its way into a parameter. The default passes it through; a store whose comparisons need an
    /// order-preserving form (DATA-0100) encodes it here, once, rather than at every call site.
    /// </summary>
    protected virtual object? EncodeScalar(object? value) => value;

    /// <summary>Turns one row into an entity. The row is keyed by whatever the server called each column.</summary>
    public TEntity Hydrate(IReadOnlyDictionary<string, object?> row)
    {
        var decoded = new Dictionary<string, object?>(StringComparer.Ordinal);
        var values = new List<MappedValue>(_readBindings.Length);
        foreach (var binding in _readBindings)
        {
            if (!row.TryGetValue(binding.PhysicalPath.Name, out var raw)) continue;
            object? value;
            if (_structuredRoots[binding.PhysicalPath.Name])
            {
                if (!decoded.TryGetValue(binding.PhysicalPath.Name, out var root))
                {
                    root = _structured.Deserialize(raw);
                    decoded.Add(binding.PhysicalPath.Name, root);
                }
                value = binding.PhysicalPath.IsNested
                    ? RelationalStructuredValueCodec.ReadPath(root, binding.PhysicalPath.Segments)
                    : root;
            }
            else value = raw is DBNull ? null : raw;
            values.Add(new MappedValue(binding.Id, binding.PhysicalPath, binding.Shape, value));
        }
        return Mapping.Hydrate<TEntity>(values);
    }

    public object? Parameter(RelationalValue value) =>
        value.Binding.Shape == MappingValueShape.Object
            ? _structured.Serialize(value.Value, value.Binding.LogicalPath.IsRoot)
            : EncodeScalar(value.Value);

    public string JsonParameter(RelationalValue value) => _structured.Serialize(value.Value);

    public string NestedRoot(IEnumerable<RelationalValue> values)
    {
        var root = new Node();
        foreach (var value in values)
        {
            var current = root;
            foreach (var segment in value.Binding.PhysicalPath.Segments)
            {
                if (!current.Children.TryGetValue(segment, out var child))
                {
                    child = new Node();
                    current.Children.Add(segment, child);
                }
                current = child;
            }
            current.Value = value.Value;
            current.HasValue = true;
        }
        return _structured.Serialize(ToObject(root));
    }

    public string ManagedPath(ResolvedField field) => ManagedPath(
        field.StorageName ?? throw new MappingValueException(Mapping.Id, "managed", "Managed storage name is missing."),
        field.ComparableType);

    public string ManagedPath(string storageName, Type type) =>
        Dialect.Read(new PhysicalPath(ManagedRoot(storageName), storageName), MappingValueShape.Scalar, type);

    /// <summary>The document column that framework-managed fields live inside.</summary>
    protected string ManagedRoot(string storageName) =>
        (Mapping.Bindings.SingleOrDefault(static binding =>
            binding.Shape == MappingValueShape.Object && binding.LogicalPath.IsRoot)
            ?? throw new MappingValueException(Mapping.Id, storageName,
                "Declare a root Object binding to persist framework-managed fields.")).PhysicalPath.Name;

    public bool IsStructuredRoot(string root) => _structuredRoots.TryGetValue(root, out var result) && result;

    public void AssignGenerated(TEntity entity, object? value)
    {
        if (_generatedSetter is null || _generatedBinding is null)
            throw new MappingValueException(Mapping.Id, Mapping.Identity.LogicalPath.ToString(),
                "This map does not declare one writable generated identity.");
        _generatedSetter(entity, _generatedBinding.Decode(value));
    }

    private static DataObject ToObject(Node node) => new(node.Children.Select(pair =>
        new DataProperty(pair.Key, pair.Value.HasValue ? pair.Value.Value : ToObject(pair.Value))));

    private sealed class Node
    {
        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
        public object? Value { get; set; }
        public bool HasValue { get; set; }
    }
}
