using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Semantics;
using Koan.Data.Relational;
using Koan.Data.Relational.Mapping;

namespace Koan.Data.Connector.MySql.Runtime;

internal sealed class MySqlEntityPlan<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly RelationalStructuredValueCodec _structured;
    private readonly MappingBindingPlan[] _readBindings;
    private readonly Dictionary<string, bool> _structuredRoots;
    private readonly Action<TEntity, object?>? _generatedSetter;
    private readonly MappingBindingPlan? _generatedBinding;

    public MySqlEntityPlan(MappingPlan mapping, MySqlRepositoryOptions options, DataSegmentationPlan segmentation)
    {
        Mapping = mapping;
        Commands = new RelationalCommandPlanner(mapping);
        Dialect = new MySqlDialect();
        Database = mapping.Container.Namespace.LastOrDefault() ?? options.Database;
        Table = mapping.Container.Name;
        QualifiedTable = $"{MySqlDialect.Quote(Database)}.{MySqlDialect.Quote(Table)}";
        _readBindings = mapping.Read().Bindings.ToArray();
        _structuredRoots = mapping.Bindings.GroupBy(static binding => binding.PhysicalPath.Name, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Any(binding => binding.Shape == MappingValueShape.Object || binding.PhysicalPath.IsNested),
                StringComparer.Ordinal);
        _structured = new RelationalStructuredValueCodec(segmentation.For(typeof(TEntity)).Fields);
        Select = string.Join(", ", _readBindings.Select(static binding => binding.PhysicalPath.Name)
            .Distinct(StringComparer.Ordinal)
            .Select(MySqlDialect.Quote));
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
    public MySqlDialect Dialect { get; }
    public string Database { get; }
    public string Table { get; }
    public string QualifiedTable { get; }
    public string Select { get; }
    public IReadOnlyList<string> IdentityRoots => Mapping.Identity.Parts.Select(static part => part.PhysicalPath.Name).ToArray();

    public TEntity Hydrate(IReadOnlyDictionary<string, object?> row)
    {
        var decoded = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
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
            : value.Value;

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

    public string ManagedPath(string storageName, Type type)
    {
        var root = Mapping.Bindings.SingleOrDefault(static binding =>
            binding.Shape == MappingValueShape.Object && binding.LogicalPath.IsRoot)
            ?? throw new MappingValueException(Mapping.Id, storageName,
                "Declare a root Object binding to persist framework-managed fields.");
        return Dialect.Read(new PhysicalPath(root.PhysicalPath.Name, storageName), MappingValueShape.Scalar, type);
    }

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
