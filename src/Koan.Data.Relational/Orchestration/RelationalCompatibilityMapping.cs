using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Optimization;

namespace Koan.Data.Relational.Orchestration;

internal static class RelationalCompatibilityMapping
{
    private static readonly HashSet<Type> Scalars =
    [
        typeof(string), typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal),
        typeof(DateTime), typeof(DateTimeOffset), typeof(Guid)
    ];

    public static MappingPlan Compile<TEntity, TKey>(
        IServiceProvider services,
        string table,
        RelationalSchemaPolicy policy)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var entityType = typeof(TEntity);
        var id = entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(static property => property.GetCustomAttribute<IdentifierAttribute>(inherit: true) is not null)
            ?? entityType.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MappingCompilationException("__relational_compatibility__", entityType, "Entity identity is missing.");
        var idPath = MappingPath.Of(id.Name);
        var idCodec = IdentityCodec<TEntity, TKey>(services);
        var key = new MappingBindingDescriptor(
            $"Key:{idPath}->Id",
            idPath,
            MappingRole.Key,
            id.PropertyType,
            new PhysicalPath("Id"),
            MappingValueShape.Scalar,
            codec: idCodec);
        var document = new MappingBindingDescriptor(
            "Object:$->Json",
            MappingPath.Root,
            MappingRole.Object,
            entityType,
            new PhysicalPath("Json"),
            MappingValueShape.Object);
        var bindings = new List<MappingBindingDescriptor> { key, document };
        if (policy.Projections is RelationalProjectionMode.ComputedProjections or RelationalProjectionMode.PhysicalColumns)
        {
            foreach (var property in entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property == id || property.GetIndexParameters().Length != 0 || IsExcluded(property)) continue;
                var effective = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (!Scalars.Contains(effective) && !effective.IsEnum) continue;
                var logical = MappingPath.Of(property.Name);
                var name = PhysicalName(property);
                bindings.Add(new MappingBindingDescriptor(
                    $"Derived:{logical}->{name}",
                    logical,
                    MappingRole.Property,
                    property.PropertyType,
                    new PhysicalPath(name),
                    MappingValueShape.Scalar,
                    MappingDirection.ReadOnly,
                    authority: MappingAuthority.Derived));
            }
        }
        return MappingPlanCompiler.Compile(new MappingDescriptor(
            "__relational_compatibility__",
            entityType,
            StorageAddress.From(table),
            new MappingIdentityDescriptor(idPath, id.PropertyType, [key]),
            bindings));
    }

    public static IRelationalStoreFeatures Features(
        IRelationalStoreFeatures inner,
        RelationalProjectionMode mode) => new CompatibilityFeatures(inner, mode);

    private static IDataMappingCodec? IdentityCodec<TEntity, TKey>(IServiceProvider services)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        if (typeof(TKey) != typeof(string)) return null;
        var optimization = services.GetStorageOptimization<TEntity, TKey>();
        if (!optimization.IsOptimized || optimization.OptimizationType != StorageOptimizationType.Guid) return null;
        return new DataMappingCodec<string, Guid>(
            value => Guid.Parse(value ?? throw new InvalidOperationException("Optimized string identity cannot be null.")),
            value => value.ToString("D"),
            "koan-string-guid-v1");
    }

    private static bool IsExcluded(PropertyInfo property) =>
        property.GetCustomAttribute<NotMappedAttribute>(inherit: true) is not null ||
        property.GetCustomAttribute<IgnoreStorageAttribute>(inherit: true) is not null;

    private static string PhysicalName(PropertyInfo property)
    {
        var column = property.GetCustomAttribute<ColumnAttribute>(inherit: true)?.Name;
        if (!string.IsNullOrWhiteSpace(column)) return column;
        var storage = property.GetCustomAttribute<StorageNameAttribute>(inherit: true)?.Name;
        return string.IsNullOrWhiteSpace(storage) ? property.Name : storage;
    }

    private sealed class CompatibilityFeatures(
        IRelationalStoreFeatures inner,
        RelationalProjectionMode mode) : IRelationalStoreFeatures
    {
        public bool SupportsJsonFunctions => inner.SupportsJsonFunctions;
        public bool SupportsPersistedComputedColumns => inner.SupportsPersistedComputedColumns;
        public bool SupportsIndexesOnComputedColumns => inner.SupportsIndexesOnComputedColumns;
        public string ProviderName => inner.ProviderName;
        public bool SupportsDefinitionValidation => inner.SupportsDefinitionValidation;
        public bool SupportsMappedIndexes => mode != RelationalProjectionMode.None &&
            (inner.SupportsMappedIndexes || inner.SupportsIndexesOnComputedColumns);
        public bool SupportsRewriteFreeExpressionIndexes => inner.SupportsRewriteFreeExpressionIndexes ||
            ((mode is RelationalProjectionMode.ComputedProjections or RelationalProjectionMode.JsonExpressionIndexes) &&
             inner.SupportsJsonFunctions && inner.SupportsIndexesOnComputedColumns);
        public bool SupportsNativeTtl => inner.SupportsNativeTtl;
    }
}
