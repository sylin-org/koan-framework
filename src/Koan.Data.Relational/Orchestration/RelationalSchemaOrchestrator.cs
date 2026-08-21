using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;

namespace Koan.Data.Relational.Orchestration;

internal sealed class RelationalSchemaOrchestrator : IRelationalSchemaOrchestrator
{
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
        var columns = mapping.Bindings
            .GroupBy(static binding => binding.PhysicalPath.Name, StringComparer.Ordinal)
            .Select(group => CompileColumn(group, identityIds))
            .ToList();
        columns.AddRange(CompileProjections(mapping, features, columns));

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
                index.Bindings.Select(static binding => new RelationalIndexPart(
                    binding.PhysicalPath, binding.PhysicalType, EncodingId(binding))),
                index.Unique,
                index.Primary,
                index.Ttl,
                rewriteFree);
        }).ToArray();

        var table = new RelationalTableDefinition(
            mapping.Container.Namespace.Count == 0
                ? ResolveSchema(policy)
                : string.Join('.', mapping.Container.Namespace),
            mapping.Container.Name,
            columns,
            mapping.Identity.Parts.Select(static part => part.PhysicalPath.Name).ToArray());
        var plan = new RelationalSchemaPlan(mapping, table, indexes, unproved);
        RelationalPlanGuard.Validate(mapping, plan);
        return plan;
    }

    public async Task<RelationalSchemaValidation> ValidateAsync(
        MappingPlan mapping,
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ddl);
        ct.ThrowIfCancellationRequested();
        var plan = Plan(mapping, features, policy);
        var unverified = plan.UnprovedClaims
            .Select(static claim => new RelationalSchemaFinding(
                claim,
                RelationalSchemaFindingKind.Unverified,
                $"Mapped intent {claim} has no proven native form on this store.",
                Corrective: false))
            .ToArray();

        var shape = await ddl.Describe(plan.Table, ct).ConfigureAwait(false);
        if (shape is null)
            return new RelationalSchemaValidation(
                plan,
                tableExists: false,
                plan.Table.Columns.Select(column => Absent(column, policy)).Concat(unverified));

        var found = new List<RelationalSchemaFinding>();
        foreach (var expected in plan.Table.Columns)
        {
            if (!shape.Columns.TryGetValue(expected.Name, out var actual))
            {
                found.Add(Absent(expected, policy));
                continue;
            }
            if (actual is null)
            {
                found.Add(new RelationalSchemaFinding(
                    expected.Name,
                    RelationalSchemaFindingKind.Unverified,
                    $"Column {expected.Name} is present; this store cannot describe its definition.",
                    Corrective: false));
                continue;
            }
            if (Matches(ddl, expected, actual)) continue;
            found.Add(new RelationalSchemaFinding(
                expected.Name,
                RelationalSchemaFindingKind.Drift,
                $"Column {expected.Name} must be {Describe(expected)}; found {actual}.",
                Corrective: IsCorrective(expected, policy)));
        }

        if (!plan.Table.Identity.SequenceEqual(shape.Identity, StringComparer.OrdinalIgnoreCase))
            found.Add(new RelationalSchemaFinding(
                "PrimaryKey",
                RelationalSchemaFindingKind.Drift,
                $"Primary key must be [{string.Join(", ", plan.Table.Identity)}]; " +
                $"found [{string.Join(", ", shape.Identity)}].",
                Corrective: true));

        // A store-level concern the neutral column model cannot express — a storage engine, a collation default.
        // It is reported in the store's own words and always stops the mapping, because a store that raises one
        // has said this container cannot serve the shape at all.
        found.AddRange(shape.Incompatible.Select(static detail => new RelationalSchemaFinding(
            "Container", RelationalSchemaFindingKind.Drift, detail, Corrective: true)));

        return new RelationalSchemaValidation(plan, tableExists: true, found.Concat(unverified));
    }

    public async Task<RelationalSchemaValidation> EnsureCreatedAsync(
        MappingPlan mapping,
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default)
    {
        var validation = await ValidateAsync(mapping, ddl, features, policy, ct).ConfigureAwait(false);
        var plan = validation.Plan;

        // Drift is not repairable by adding columns, so it is reported before consent is even considered:
        // whether Koan may issue DDL has no bearing on a table that is already the wrong shape.
        var drift = validation.Findings
            .Where(static finding => finding.Kind == RelationalSchemaFindingKind.Drift && finding.Corrective)
            .ToArray();
        if (drift.Length != 0)
            throw new SchemaMismatchException(
                mapping.EntityType.FullName ?? mapping.EntityType.Name,
                plan.Table,
                policy.Matching,
                drift,
                IsDdlAllowed(policy));

        var absent = validation.Absent
            .Select(finding => plan.Table.Columns.First(column =>
                string.Equals(column.Name, finding.Subject, StringComparison.Ordinal)))
            .ToArray();

        // No ALTER makes an existing table key on a column it does not have, so an absent identity column on a
        // table that exists is a mismatch to report, not work to schedule.
        if (validation.TableExists && absent.Any(static column => column.IsIdentity))
            throw new SchemaMismatchException(
                mapping.EntityType.FullName ?? mapping.EntityType.Name,
                plan.Table,
                policy.Matching,
                validation.Absent.Where(finding => absent.Any(column =>
                    column.IsIdentity && string.Equals(column.Name, finding.Subject, StringComparison.Ordinal)))
                    .ToArray(),
                IsDdlAllowed(policy));

        if (!validation.TableExists || absent.Length != 0)
        {
            EnsureDdlAllowed(policy, features, plan.Table);
            if (!validation.TableExists)
            {
                await ddl.Create(plan.Table, ct).ConfigureAwait(false);
            }
            else
            {
                foreach (var column in absent)
                    await ddl.AddColumn(plan.Table, column, ct).ConfigureAwait(false);
            }
        }

        if (features.SupportsMappedIndexes && IsDdlAllowed(policy))
        {
            foreach (var index in plan.Indexes.Where(static index => !index.Primary))
            {
                if (index.Ttl && !features.SupportsNativeTtl) continue;
                if (!index.RewriteFree && index.IsExpression) continue;
                await ddl.CreateIndex(plan.Table, index, ct).ConfigureAwait(false);
            }
        }

        return await ValidateAsync(mapping, ddl, features, policy, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A column the store does not hold.
    ///
    /// <para>A projected column exists so the planner can reach a mapped value directly; reads still resolve
    /// through the structured root without it. Relaxed matching therefore tolerates its absence, which is the
    /// one place MySQL's private validation was right to be lenient and the other three had no opinion.</para>
    /// </summary>
    private static RelationalSchemaFinding Absent(RelationalColumnDefinition column, RelationalSchemaPolicy policy) =>
        new(column.Name,
            RelationalSchemaFindingKind.Absent,
            $"Column {column.Name} is missing.",
            Corrective: policy.Matching == RelationalSchemaMatchingMode.Strict || !column.IsProjected);

    /// <summary>
    /// Whether drift on this column stops the mapping. Identity and the structured document cannot drift on any
    /// matching mode — one addresses rows and the other holds them — and Strict tolerates no drift at all.
    /// </summary>
    private static bool IsCorrective(RelationalColumnDefinition column, RelationalSchemaPolicy policy) =>
        policy.Matching == RelationalSchemaMatchingMode.Strict ||
        column.IsIdentity ||
        column.Shape == RelationalStorageShape.Structured;

    private static RelationalColumnDefinition CompileColumn(
        IGrouping<string, MappingBindingPlan> group,
        IReadOnlySet<string> identityIds)
    {
        var bindings = group.ToArray();
        var identity = bindings.All(binding => identityIds.Contains(binding.Id));
        var generated = bindings.All(static binding =>
            binding.Descriptor.Generation == MappingGeneration.Provider);
        var structured = bindings.Any(static binding =>
            binding.Shape == MappingValueShape.Object || binding.PhysicalPath.IsNested);
        return new RelationalColumnDefinition(
            group.Key,
            structured ? typeof(DataObject) : bindings[0].PhysicalType,
            Shape: structured ? RelationalStorageShape.Structured : RelationalStorageShape.Scalar,
            IsIdentity: identity,
            IsGenerated: generated);
    }

    /// <summary>
    /// Columns the store computes out of the structured root, so a filter or an order on a mapped value reaches
    /// a materialized column instead of reading the document.
    ///
    /// <para>Two adapters built these and two did not, from private copies of one predicate, and the framework
    /// could not see them at all. The decision is single — every scalar property that has no physical column of
    /// its own — and a store answers only whether it can hold one.</para>
    /// </summary>
    private static IEnumerable<RelationalColumnDefinition> CompileProjections(
        MappingPlan mapping,
        IRelationalStoreFeatures features,
        IReadOnlyList<RelationalColumnDefinition> physical)
    {
        if (!features.SupportsPersistedComputedColumns || !features.SupportsJsonFunctions) yield break;
        if (!mapping.Bindings.Any(static binding =>
                binding.Shape == MappingValueShape.Object && binding.LogicalPath.IsRoot)) yield break;

        var taken = physical.Select(static column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in mapping.Bindings.Where(binding =>
                     binding.Descriptor.Authority == MappingAuthority.Derived &&
                     binding.Shape == MappingValueShape.Scalar &&
                     binding.LogicalPath.Segments.Count == 1 &&
                     !taken.Contains(binding.LogicalPath.Leaf)))
            yield return new RelationalColumnDefinition(
                binding.LogicalPath.Leaf,
                binding.PhysicalType,
                IsProjected: true,
                ProjectedFrom: binding.PhysicalPath);
    }

    private static string EncodingId(MappingBindingPlan binding) =>
        binding.Descriptor.Codec?.Id ?? $"clr:{binding.PhysicalType.AssemblyQualifiedName}";

    private static string ResolveSchema(RelationalSchemaPolicy policy) =>
        string.IsNullOrWhiteSpace(policy.DefaultSchema) ? "dbo" : policy.DefaultSchema;

    private static void EnsureDdlAllowed(
        RelationalSchemaPolicy policy,
        IRelationalStoreFeatures features,
        RelationalTableDefinition table)
    {
        if (IsDdlAllowed(policy)) return;
        var reason = policy.StorageLifecycle != StorageLifecycle.Managed
            ? $"StorageLifecycle={policy.StorageLifecycle} forbids shape mutation."
            : policy.Access != DataSourceAccess.ReadWrite
                ? $"Access={policy.Access} forbids shape mutation."
                : policy.Ddl != RelationalDdlPolicy.AutoCreate
                    ? $"DDL is disabled by policy '{policy.Ddl}'."
                    : RelationalDdlGate.Refusal;
        throw new InvalidOperationException(
            $"Relational schema creation was rejected for {features.ProviderName}/{table}. {reason}");
    }

    private static bool IsDdlAllowed(RelationalSchemaPolicy policy) =>
        policy.StorageLifecycle == StorageLifecycle.Managed &&
        policy.Access == DataSourceAccess.ReadWrite &&
        policy.Ddl == RelationalDdlPolicy.AutoCreate &&
        RelationalDdlGate.Allowed(policy.AllowProductionDdl);

    /// <summary>
    /// Three questions, each with one owner. Whether the column is the right one is the store's to judge, in its
    /// own vocabulary; whether the store or the writer supplies the value is the mapping's, and every store
    /// reports it the same way.
    ///
    /// <para>Identity membership is not among them: the primary key is one ordered decision, checked once,
    /// rather than a per-column opinion that reports the same difference several times over.</para>
    /// </summary>
    private static bool Matches(
        IRelationalDdlExecutor ddl,
        RelationalColumnDefinition expected,
        RelationalColumnState actual) =>
        ddl.ColumnMatches(expected, actual) &&
        expected.IsGenerated == actual.IsGenerated &&
        expected.IsProjected == actual.IsProjected;

    private static string Describe(RelationalColumnDefinition column) =>
        $"{column.Shape}/{column.ClrType.Name}/" +
        $"generated={column.IsGenerated}/projected={column.IsProjected}";
}
