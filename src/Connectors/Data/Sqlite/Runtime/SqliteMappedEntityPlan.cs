using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Relational;
using Koan.Data.Relational.Mapping;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteMappedEntityPlan<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly MappingPlan _mapping;
    private readonly MappingBindingPlan[] _bindings;
    private readonly string[] _roots;

    public SqliteMappedEntityPlan(MappingPlan mapping)
    {
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        if (mapping.Container.Namespace.Count > 1 ||
            mapping.Container.Namespace.Count == 1 &&
            !string.Equals(mapping.Container.Namespace[0], "main", StringComparison.OrdinalIgnoreCase))
            throw new MappingCompilationException(mapping.Source, mapping.EntityType,
                "SQLite mappings support the empty namespace or 'main'.");
        _bindings = mapping.Bindings.ToArray();
        _roots = _bindings.Select(binding => binding.PhysicalPath.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var identity in mapping.Identity.Parts)
        {
            var binding = _bindings.Single(candidate => candidate.Id == identity.Id);
            if (binding.PhysicalPath.IsNested || binding.Shape != MappingValueShape.Scalar)
                throw new MappingCompilationException(mapping.Source, mapping.EntityType,
                    "SQLite identity parts require scalar physical names.");
        }
    }

    public MappingPlan Mapping => _mapping;
    public string Table => _mapping.Container.Name;
    public IReadOnlyList<string> Roots => _roots;
    public IReadOnlyList<MappingBindingPlan> Bindings => _bindings;
    public string Select => string.Join(", ", _roots.Select(root => $"koan_row.{SqliteDialect.Quote(root)}"));
    public string IdentityName => _mapping.Identity.LogicalPath.Segments.Last();
    public bool IsGeneratedIdentity => _mapping.Identity.IsGenerated;
    public IReadOnlyList<string> IdentityRoots => _mapping.Identity.Parts
        .Select(part => part.PhysicalPath.Name)
        .ToArray();

    public IReadOnlyList<RelationalValue> Identity(TKey id) =>
        new RelationalCommandPlanner(_mapping).Delete(id).Identity;

    public SqliteMappedWrite Write(TEntity entity, bool includeGeneratedIdentity = false)
    {
        var record = _mapping.Write(entity);
        var bindings = _bindings.ToDictionary(binding => binding.Id, StringComparer.Ordinal);
        var direct = new Dictionary<string, object?>(StringComparer.Ordinal);
        var nested = new Dictionary<string, List<(IReadOnlyList<string> Path, object? Value)>>(StringComparer.Ordinal);
        foreach (var value in record.Values)
        {
            var binding = bindings[value.BindingId];
            if (value.Path.IsNested)
            {
                if (!nested.TryGetValue(value.Path.Name, out var values))
                    nested.Add(value.Path.Name, values = []);
                values.Add((value.Path.Segments, value.Value));
                continue;
            }
            direct.Add(value.Path.Name, binding.Shape == MappingValueShape.Object
                ? SqliteStructuredValues.Serialize(value.Value)
                : ComparableScalarEncoding.EncodeComparand(value.Value));
        }
        foreach (var (root, values) in nested)
            direct.Add(root, SqliteStructuredValues.Build(values).ToJsonString());
        if (includeGeneratedIdentity)
        {
            foreach (var value in _mapping.WriteIdentity(entity.Id).Values)
            {
                var binding = bindings[value.BindingId];
                direct.Add(value.Path.Name, binding.Shape == MappingValueShape.Object
                    ? SqliteStructuredValues.Serialize(value.Value)
                    : ComparableScalarEncoding.EncodeComparand(value.Value));
            }
        }
        return new SqliteMappedWrite(direct, nested.Keys.ToHashSet(StringComparer.Ordinal));
    }

    public TEntity Read(SqliteDataReader reader)
    {
        var roots = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var root in _roots)
        {
            var ordinal = reader.GetOrdinal(root);
            roots[root] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
        }

        var structured = new Dictionary<string, object?>(StringComparer.Ordinal);
        var values = new MappedValue[_bindings.Length];
        for (var index = 0; index < _bindings.Length; index++)
        {
            var binding = _bindings[index];
            var raw = roots[binding.PhysicalPath.Name];
            object? value;
            if (binding.PhysicalPath.IsNested)
            {
                if (!structured.TryGetValue(binding.PhysicalPath.Name, out var root))
                {
                    root = SqliteStructuredValues.Deserialize(raw);
                    structured.Add(binding.PhysicalPath.Name, root);
                }
                value = SqliteStructuredValues.ReadPath(root, binding.PhysicalPath.Segments);
            }
            else
                value = binding.Shape == MappingValueShape.Object
                    ? SqliteStructuredValues.Deserialize(raw)
                    : raw;
            values[index] = new MappedValue(binding.Id, binding.PhysicalPath, binding.Shape, value);
        }
        return _mapping.Hydrate<TEntity>(values);
    }

    public void ApplyGeneratedIdentity(TEntity entity, object? value)
    {
        if (!IsGeneratedIdentity || _mapping.Identity.IsComposite)
            throw new InvalidOperationException("SQLite generated identity requires one generated key binding.");
        var binding = _bindings.Single(candidate => candidate.Id == _mapping.Identity.Parts[0].Id);
        var decoded = binding.Decode(value);
        var property = typeof(TEntity).GetProperty(_mapping.Identity.LogicalPath.Segments.Single())
            ?? throw new MappingValueException(_mapping.Id, _mapping.Identity.LogicalPath.ToString(),
                "Generated identity property is unavailable.");
        property.SetValue(entity, decoded);
    }
}
