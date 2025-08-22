using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Abstractions.Naming;
using Sora.Data.Core;
using System.Reflection;

namespace Sora.Data.Relational.Schema;

public sealed class RelationalSchemaModel : IRelationalSchemaModel
{
    public RelationalSchemaModel(RelationalTable table) => Table = table;
    public RelationalTable Table { get; }
}

public static class RelationalModelBuilder
{
    public static IRelationalSchemaModel FromEntity(Type entityType)
    {
        var storage = entityType.GetCustomAttribute<StorageAttribute>();
        var tableName = !string.IsNullOrWhiteSpace(storage?.Name) ? storage!.Name! : ResolveDefaultName(entityType);
        var ns = storage?.Namespace;

        var idSpec = AggregateMetadata.GetIdSpec(entityType)?.Prop ?? throw new InvalidOperationException($"No Identifier/Id on {entityType.Name}");
        var idCol = BuildColumn(idSpec);

        var props = entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p != idSpec && p.GetCustomAttribute<IgnoreStorageAttribute>() is null)
            .ToArray();
        var cols = props.Select(BuildColumn).ToList();

        var indexes = new List<RelationalIndex>();
        // Implicit PK index represented separately
        var pkIndex = new RelationalIndex(null, new[] { idCol }, true, true);
        indexes.Add(pkIndex);

        // Declared secondary indexes
        foreach (var ix in IndexMetadata.GetIndexes(entityType).Where(i => !i.IsPrimaryKey))
        {
            var ixCols = ix.Properties.Select(p => p == idSpec ? idCol : cols.First(c => c.SourceProperty == p)).ToList();
            indexes.Add(new RelationalIndex(ix.Name, ixCols, ix.Unique, false));
        }

        var table = new RelationalTable(tableName, ns, idCol, cols, indexes);
        return new RelationalSchemaModel(table);
    }

    private static string ResolveDefaultName(Type entityType)
    {
        // Relational defaults: EntityType, '_' separator, AsIs casing when conventions apply
        var conv = new StorageNameResolver.Convention(StorageNamingStyle.EntityType, "_", NameCasing.AsIs);
        return StorageNameResolver.Resolve(entityType, conv);
    }

    private static RelationalColumn BuildColumn(PropertyInfo p)
    {
        var sn = p.GetCustomAttribute<StorageNameAttribute>();
        var name = sn?.Name ?? p.Name;
        var clr = p.PropertyType;
        var underlying = Nullable.GetUnderlyingType(clr) ?? clr;
        var isNullable = Nullable.GetUnderlyingType(p.PropertyType) is not null || (!underlying.IsValueType && underlying != typeof(string));
        var isJson = TypeClassification.IsComplex(underlying);
        return new RelationalColumn(name, underlying, isNullable, isJson, p);
    }
}
