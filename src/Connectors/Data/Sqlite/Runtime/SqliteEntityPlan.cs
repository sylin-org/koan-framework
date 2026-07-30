using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Semantics;
using Koan.Data.Relational;
using Koan.Data.Relational.Mapping;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteEntityPlan<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly RelationalStructuredValueCodec _structured;
    private readonly MappingBindingPlan[] _readBindings;
    private readonly Dictionary<string, bool> _structuredRoots;
    private readonly Action<TEntity, object?>? _generatedSetter;
    private readonly MappingBindingPlan? _generatedBinding;

    internal SqliteEntityPlan(MappingPlan mapping, DataSegmentationPlan segmentation)
    {
        if (mapping.Container.Namespace.Count > 1 ||
            mapping.Container.Namespace.Count == 1 &&
            !string.Equals(mapping.Container.Namespace[0], "main", StringComparison.OrdinalIgnoreCase))
            throw new MappingCompilationException(mapping.Source, mapping.EntityType,
                "SQLite mappings support the empty namespace or 'main'.");

        Mapping = mapping;
        Commands = new RelationalCommandPlanner(mapping);
        Dialect = new SqliteDialect();
        Table = mapping.Container.Name;
        QualifiedTable = SqliteDialect.Quote(Table);
        _readBindings = mapping.Read().Bindings.ToArray();
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
        Select = string.Join(", ", Roots.Select(root => $"koan_row.{SqliteDialect.Quote(root)}"));
        IdentityRoots = mapping.Identity.Parts.Select(static part => part.PhysicalPath.Name).ToArray();

        foreach (var identity in mapping.Identity.Parts)
        {
            var binding = mapping.Bindings.Single(candidate => candidate.Id == identity.Id);
            if (binding.PhysicalPath.IsNested || binding.Shape != MappingValueShape.Scalar)
                throw new MappingCompilationException(mapping.Source, mapping.EntityType,
                    "SQLite identity components require scalar physical names.");
        }

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

    internal MappingPlan Mapping { get; }
    internal RelationalCommandPlanner Commands { get; }
    internal SqliteDialect Dialect { get; }
    internal string Table { get; }
    internal string QualifiedTable { get; }
    internal string Select { get; }
    internal IReadOnlyList<string> Roots { get; }
    internal IReadOnlyList<string> IdentityRoots { get; }

    internal TEntity Hydrate(SqliteDataReader reader)
    {
        var roots = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var root in Roots)
        {
            var ordinal = reader.GetOrdinal(root);
            roots[root] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
        }

        var decoded = new Dictionary<string, object?>(StringComparer.Ordinal);
        var values = new List<MappedValue>(_readBindings.Length);
        foreach (var binding in _readBindings)
        {
            if (!roots.TryGetValue(binding.PhysicalPath.Name, out var raw)) continue;
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
            else value = raw;
            values.Add(new MappedValue(binding.Id, binding.PhysicalPath, binding.Shape, value));
        }
        return Mapping.Hydrate<TEntity>(values);
    }

    internal object? Parameter(RelationalValue value) => value.Binding.Shape == MappingValueShape.Object
        ? _structured.Serialize(value.Value, value.Binding.LogicalPath.IsRoot)
        : ComparableScalarEncoding.EncodeComparand(value.Value);

    internal string JsonParameter(RelationalValue value) =>
        _structured.Serialize(value.Value, includeManagedFields: false);

    internal string NestedRoot(IEnumerable<RelationalValue> values)
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

    internal string ManagedPath(ResolvedField field) => ManagedPath(
        field.StorageName ?? throw new MappingValueException(Mapping.Id, "managed", "Managed storage name is missing."),
        field.ComparableType);

    internal string ManagedPath(string storageName, Type type)
    {
        var root = Mapping.Bindings.SingleOrDefault(static binding =>
            binding.Shape == MappingValueShape.Object && binding.LogicalPath.IsRoot)
            ?? throw new MappingValueException(Mapping.Id, storageName,
                "Declare a root Object binding to persist framework-managed fields.");
        return Dialect.Read(new PhysicalPath(root.PhysicalPath.Name, storageName), MappingValueShape.Scalar, type);
    }

    internal bool IsStructuredRoot(string root) => _structuredRoots.TryGetValue(root, out var value) && value;

    internal void AssignGenerated(TEntity entity, object? value)
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
        internal Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
        internal object? Value { get; set; }
        internal bool HasValue { get; set; }
    }
}
