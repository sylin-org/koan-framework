using Koan.Data.Abstractions;
using Koan.Data.Core;

namespace Koan.Data.Relational.Orchestration;

internal sealed class RelationalSchemaOrchestrator :
    IRelationalSchemaOrchestrator,
    IRelationalMappingSchemaOrchestrator
{
    private readonly IServiceProvider _services;

    public RelationalSchemaOrchestrator(IServiceProvider services) => _services = services;

    public RelationalSchemaPlan Plan(
        MappingPlan mapping,
        IRelationalStoreFeatures features,
        RelationalSchemaPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(policy);

        var identityIds = mapping.Identity.Parts
            .Select(static part => part.Id)
            .ToHashSet(StringComparer.Ordinal);
        var indexRoots = mapping.Indexes
            .SelectMany(static index => index.Bindings)
            .Select(static binding => binding.PhysicalPath.Name)
            .ToHashSet(StringComparer.Ordinal);
        var columns = mapping.Bindings
            .GroupBy(static binding => binding.PhysicalPath.Name, StringComparer.Ordinal)
            .Select(group => CompileColumn(group, identityIds, indexRoots, features, policy))
            .ToArray();

        var unproved = new List<string>();
        var indexes = mapping.Indexes.Select(index =>
        {
            var nested = index.Bindings.Any(static binding => binding.PhysicalPath.IsNested);
            var derived = index.Bindings.Any(static binding =>
                binding.Descriptor.Authority == MappingAuthority.Derived);
            var rewriteFree = !derived || features.SupportsRewriteFreeExpressionIndexes;
            if (!index.Primary && !features.SupportsMappedIndexes)
                unproved.Add($"Index:{index.Name}");
            if (derived && !rewriteFree)
                unproved.Add($"RewriteFree:{index.Name}");
            if (nested && !features.SupportsRewriteFreeExpressionIndexes)
                unproved.Add($"ExpressionIndex:{index.Name}");
            if (index.Ttl && !features.SupportsNativeTtl)
                unproved.Add($"TTL:{index.Name}");
            return new RelationalIndexDefinition(
                index.Name,
                index.Bindings.Select(static binding => binding.PhysicalPath),
                index.Bindings.Select(EncodingId),
                index.Unique,
                index.Primary,
                index.Ttl,
                rewriteFree);
        }).ToArray();

        var schema = mapping.Container.Namespace.Count == 0
            ? ResolveSchema(policy)
            : string.Join('.', mapping.Container.Namespace);
        var plan = new RelationalSchemaPlan(
            mapping,
            schema,
            mapping.Container.Name,
            columns,
            indexes,
            unproved);
        RelationalPlanGuard.Validate(mapping, plan);
        return plan;
    }

    public Task<RelationalSchemaValidation> ValidateAsync(
        MappingPlan mapping,
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var plan = Plan(mapping, features, policy);
        var exists = ddl.TableExists(plan.Schema, plan.Table);
        var missing = new List<string>();
        var incompatible = new List<string>();
        var unverified = new List<string>();
        if (!exists)
        {
            missing.AddRange(plan.Columns.Select(static column => column.Name));
            return Task.FromResult(new RelationalSchemaValidation(
                plan,
                tableExists: false,
                missing,
                incompatible,
                unverified));
        }

        foreach (var expected in plan.Columns)
        {
            var actual = ddl.DescribeColumn(plan.Schema, plan.Table, expected.Name);
            if (actual is null)
            {
                missing.Add(expected.Name);
                continue;
            }
            if (!features.SupportsDefinitionValidation)
            {
                unverified.Add($"ColumnDefinition:{expected.Name}");
                continue;
            }
            if (!DefinitionEquals(expected, actual))
                incompatible.Add($"{expected.Name}: expected {Describe(expected)}; found {Describe(actual)}");
        }
        return Task.FromResult(new RelationalSchemaValidation(
            plan,
            tableExists: true,
            missing,
            incompatible,
            unverified));
    }

    public async Task<RelationalSchemaValidation> EnsureCreatedAsync(
        MappingPlan mapping,
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default)
    {
        var validation = await ValidateAsync(mapping, ddl, features, policy, ct).ConfigureAwait(false);
        if (!validation.IsCompatible)
        {
            EnsureDdlAllowed(policy, features, validation.Plan.Schema, validation.Plan.Table);
            if (validation.Incompatible.Count != 0)
            {
                throw new SchemaMismatchException(
                    mapping.EntityType.FullName ?? mapping.EntityType.Name,
                    validation.Plan.Table,
                    policy.Matching.ToString(),
                    validation.Missing.Concat(validation.Incompatible).ToArray(),
                    [],
                    IsDdlAllowed(policy));
            }

            if (!validation.TableExists)
            {
                ddl.CreateTableWithColumns(
                    validation.Plan.Schema,
                    validation.Plan.Table,
                    validation.Plan.Columns);
            }
            else
            {
                foreach (var column in validation.Plan.Columns.Where(column =>
                             validation.Missing.Contains(column.Name, StringComparer.Ordinal)))
                {
                    if (column.IsComputed && column.JsonPath is not null)
                    {
                        ddl.AddComputedColumnFromJson(
                            validation.Plan.Schema,
                            validation.Plan.Table,
                            column.Name,
                            column.JsonPath,
                            features.SupportsPersistedComputedColumns);
                    }
                    else
                    {
                        ddl.AddMappedColumn(validation.Plan.Schema, validation.Plan.Table, column);
                    }
                }
            }
        }

        if (features.SupportsMappedIndexes && IsDdlAllowed(policy))
        {
            foreach (var index in validation.Plan.Indexes.Where(static index => !index.Primary))
            {
                if (index.Ttl && !features.SupportsNativeTtl) continue;
                if (!index.RewriteFree && index.Parts.Any(static part => part.IsNested)) continue;
                ddl.CreateMappedIndex(validation.Plan.Schema, validation.Plan.Table, index);
            }
        }

        return await ValidateAsync(mapping, ddl, features, policy, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, object?>> ValidateAsync<TEntity, TKey>(
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        string table,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var mapping = RelationalCompatibilityMapping.Compile<TEntity, TKey>(_services, table, policy);
        var compatibleFeatures = RelationalCompatibilityMapping.Features(features, policy.Projections);
        var validation = await ValidateAsync(mapping, ddl, compatibleFeatures, policy, ct).ConfigureAwait(false);
        var state = validation.IsCompatible
            ? validation.Unverified.Count == 0 ? "Healthy" : "Degraded"
            : policy.Matching == RelationalSchemaMatchingMode.Strict ? "Unhealthy" : "Degraded";
        return new Dictionary<string, object?>
        {
            ["Provider"] = features.ProviderName,
            ["Schema"] = validation.Plan.Schema,
            ["Table"] = validation.Plan.Table,
            ["TableExists"] = validation.TableExists,
            ["ProjectedColumns"] = validation.Plan.Columns.Select(static column => column.Name).ToArray(),
            ["MissingColumns"] = validation.Missing.ToArray(),
            ["Policy"] = policy.Projections.ToString(),
            ["DdlAllowed"] = IsDdlAllowed(policy),
            ["MatchingMode"] = policy.Matching.ToString(),
            ["State"] = state
        };
    }

    public async Task EnsureCreatedAsync<TEntity, TKey>(
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        string table,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var mapping = RelationalCompatibilityMapping.Compile<TEntity, TKey>(_services, table, policy);
        var compatibleFeatures = RelationalCompatibilityMapping.Features(features, policy.Projections);
        await EnsureCreatedAsync(mapping, ddl, compatibleFeatures, policy, ct).ConfigureAwait(false);
    }

    private static RelationalColumnDefinition CompileColumn(
        IGrouping<string, MappingBindingPlan> group,
        IReadOnlySet<string> identityIds,
        IReadOnlySet<string> indexRoots,
        IRelationalStoreFeatures features,
        RelationalSchemaPolicy policy)
    {
        var bindings = group.ToArray();
        var identity = bindings.All(binding => identityIds.Contains(binding.Id));
        var generated = bindings.All(static binding =>
            binding.Descriptor.Generation == MappingGeneration.Provider);
        var computed = policy.Projections == RelationalProjectionMode.ComputedProjections &&
                       features.SupportsJsonFunctions &&
                       bindings.Length == 1 &&
                       bindings[0].Descriptor.Authority == MappingAuthority.Derived &&
                       !bindings[0].LogicalPath.IsRoot;
        var structured = !computed && bindings.Any(static binding =>
            binding.Shape == MappingValueShape.Object || binding.PhysicalPath.IsNested);
        var nullable = !identity && (computed || bindings.All(binding => IsNullable(binding.LogicalType)));
        var jsonPath = computed
            ? "$." + string.Join('.', bindings[0].LogicalPath.Segments)
            : null;
        return new RelationalColumnDefinition(
            group.Key,
            computed ? typeof(string) : structured ? typeof(DataObject) : bindings[0].PhysicalType,
            nullable,
            IsComputed: computed,
            JsonPath: jsonPath,
            IsIndexed: indexRoots.Contains(group.Key),
            Shape: structured ? RelationalStorageShape.Structured : RelationalStorageShape.Scalar,
            IsIdentity: identity,
            IsGenerated: generated);
    }

    private static string EncodingId(MappingBindingPlan binding) =>
        binding.Descriptor.Codec?.Id ?? $"clr:{binding.PhysicalType.AssemblyQualifiedName}";

    private static string ResolveSchema(RelationalSchemaPolicy policy) =>
        string.IsNullOrWhiteSpace(policy.DefaultSchema) ? "dbo" : policy.DefaultSchema;

    private static void EnsureDdlAllowed(
        RelationalSchemaPolicy policy,
        IRelationalStoreFeatures features,
        string schema,
        string table)
    {
        if (IsDdlAllowed(policy)) return;
        var reason = policy.StorageLifecycle == Koan.Data.Abstractions.Sources.StorageLifecycle.External
            ? "StorageLifecycle=External forbids shape mutation."
            : policy.Ddl != RelationalDdlPolicy.AutoCreate
                ? $"DDL is disabled by policy '{policy.Ddl}'."
                : RelationalDdlGate.Refusal;
        throw new InvalidOperationException(
            $"Relational schema creation was rejected for {features.ProviderName}/{schema}/{table}. {reason}");
    }

    private static bool IsDdlAllowed(RelationalSchemaPolicy policy) =>
        policy.StorageLifecycle == Koan.Data.Abstractions.Sources.StorageLifecycle.Managed &&
        policy.Ddl == RelationalDdlPolicy.AutoCreate &&
        RelationalDdlGate.Allowed(policy.AllowProductionDdl);

    private static bool DefinitionEquals(RelationalColumnDefinition expected, RelationalColumnDefinition actual) =>
        expected.ClrType == actual.ClrType &&
        expected.Nullable == actual.Nullable &&
        expected.IsComputed == actual.IsComputed &&
        string.Equals(expected.JsonPath, actual.JsonPath, StringComparison.Ordinal) &&
        expected.Shape == actual.Shape &&
        expected.IsIdentity == actual.IsIdentity &&
        expected.IsGenerated == actual.IsGenerated;

    private static string Describe(RelationalColumnDefinition column) =>
        $"{column.Shape}/{column.ClrType.Name}/nullable={column.Nullable}/computed={column.IsComputed}/" +
        $"path={column.JsonPath ?? "<none>"}/identity={column.IsIdentity}/generated={column.IsGenerated}";

    private static bool IsNullable(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
}
