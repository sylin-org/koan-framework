using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Relational.Orchestration;
using MySqlConnector;

namespace Koan.Data.Connector.MySql.Runtime;

internal static class MySqlSchema
{
    private const string RequiredEngine = "InnoDB";

    public static async Task Provision<TEntity, TKey>(
        MySqlConnection connection,
        MySqlEntityPlan<TEntity, TKey> plan,
        MySqlRepositoryOptions options,
        CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        if (await Exists(connection, plan.Database, plan.Table, ct).ConfigureAwait(false))
        {
            await ValidateShape<TEntity, TKey>(connection, plan, options, ct).ConfigureAwait(false);
            return;
        }
        if (!DdlAllowed(options))
            throw Mismatch<TEntity, TKey>(plan, options, [$"Table '{plan.QualifiedTable}' does not exist."], []);

        await using var command = connection.CreateCommand();
        command.CommandText = CreateSql(plan);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await ValidateShape<TEntity, TKey>(connection, plan, options, ct).ConfigureAwait(false);
    }

    public static async Task Validate<TEntity, TKey>(
        MySqlConnection connection,
        MySqlEntityPlan<TEntity, TKey> plan,
        MySqlRepositoryOptions options,
        CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        if (!await Exists(connection, plan.Database, plan.Table, ct).ConfigureAwait(false))
            throw Mismatch<TEntity, TKey>(plan, options, [$"Table '{plan.QualifiedTable}' does not exist."], []);
        await ValidateShape<TEntity, TKey>(connection, plan, options, ct).ConfigureAwait(false);
    }

    public static async Task<bool> Exists(MySqlConnection connection, string database, string table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM information_schema.tables WHERE table_schema=@database AND table_name=@table LIMIT 1";
        command.Parameters.AddWithValue("database", database);
        command.Parameters.AddWithValue("table", table);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private static async Task ValidateShape<TEntity, TKey>(
        MySqlConnection connection,
        MySqlEntityPlan<TEntity, TKey> plan,
        MySqlRepositoryOptions options,
        CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var actual = await Describe(connection, plan.Database, plan.Table, ct).ConfigureAwait(false);
        var expected = ExpectedColumns(plan);
        var missing = new List<string>();
        var incompatible = new List<string>();

        if (!string.Equals(actual.Engine, RequiredEngine, StringComparison.OrdinalIgnoreCase))
            incompatible.Add($"Engine must be {RequiredEngine}; found {actual.Engine ?? "unknown"}.");

        var strict = options.SchemaMatching == RelationalSchemaMatchingMode.Strict;
        foreach (var column in expected)
        {
            if (!actual.Columns.TryGetValue(column.Name, out var found))
            {
                if (column.Required || strict) missing.Add($"Missing {(column.Required ? "required " : string.Empty)}column '{column.Name}'.");
                continue;
            }

            if ((strict || column.Critical) && !TypeMatches(found, column.StoreType))
                incompatible.Add($"Column '{column.Name}' must be {column.StoreType}; found {Describe(found)}.");
            if ((strict || column.Identity) && found.Nullable != column.Nullable)
                incompatible.Add($"Column '{column.Name}' nullability differs: expected nullable={column.Nullable}, found nullable={found.Nullable}.");
            if ((strict || column.Identity) && found.AutoIncrement != column.AutoIncrement)
                incompatible.Add($"Column '{column.Name}' auto-increment shape differs: expected={column.AutoIncrement}, found={found.AutoIncrement}.");
            if (strict && found.StoredGenerated != column.StoredGenerated)
                incompatible.Add($"Column '{column.Name}' generated-column shape differs: expected stored={column.StoredGenerated}, found stored={found.StoredGenerated}.");
        }

        var expectedKey = plan.IdentityRoots.ToArray();
        var actualKey = actual.Columns.Values
            .Where(static column => column.PrimaryOrdinal is not null)
            .OrderBy(static column => column.PrimaryOrdinal)
            .Select(static column => column.Name)
            .ToArray();
        if (!expectedKey.SequenceEqual(actualKey, StringComparer.OrdinalIgnoreCase))
            incompatible.Add($"Primary key must be [{string.Join(", ", expectedKey)}]; found [{string.Join(", ", actualKey)}].");

        if (missing.Count != 0 || incompatible.Count != 0)
            throw Mismatch<TEntity, TKey>(plan, options, missing, incompatible);
    }

    private static async Task<TableShape> Describe(
        MySqlConnection connection,
        string database,
        string table,
        CancellationToken ct)
    {
        string? engine;
        await using (var tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText = "SELECT engine FROM information_schema.tables WHERE table_schema=@database AND table_name=@table LIMIT 1";
            tableCommand.Parameters.AddWithValue("database", database);
            tableCommand.Parameters.AddWithValue("table", table);
            engine = Convert.ToString(await tableCommand.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        var columns = new Dictionary<string, ColumnShape>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.column_name, c.data_type, c.column_type, c.is_nullable,
                   c.character_set_name, c.collation_name, c.extra, pk.ordinal_position
              FROM information_schema.columns AS c
              LEFT JOIN information_schema.key_column_usage AS pk
                ON pk.table_schema = c.table_schema
               AND pk.table_name = c.table_name
               AND pk.column_name = c.column_name
               AND pk.constraint_name = 'PRIMARY'
             WHERE c.table_schema = @database AND c.table_name = @table
             ORDER BY c.ordinal_position
            """;
        command.Parameters.AddWithValue("database", database);
        command.Parameters.AddWithValue("table", table);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var extra = Convert.ToString(reader.GetValue(6)) ?? string.Empty;
            var shape = new ColumnShape(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                string.Equals(reader.GetString(3), "YES", StringComparison.OrdinalIgnoreCase),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
                extra.Contains("stored generated", StringComparison.OrdinalIgnoreCase),
                reader.IsDBNull(7) ? null : Convert.ToInt32(reader.GetValue(7)));
            columns.Add(shape.Name, shape);
        }
        return new TableShape(engine, columns);
    }

    private static IReadOnlyList<ExpectedColumn> ExpectedColumns<TEntity, TKey>(MySqlEntityPlan<TEntity, TKey> plan)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var columns = new List<ExpectedColumn>();
        var rootNames = plan.Mapping.Bindings.Select(static binding => binding.PhysicalPath.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var root in plan.Mapping.Bindings.GroupBy(static binding => binding.PhysicalPath.Name, StringComparer.Ordinal))
        {
            var identity = root.FirstOrDefault(binding => plan.Mapping.Identity.Parts.Any(part => part.Id == binding.Id));
            var structured = root.Any(binding => binding.Shape == MappingValueShape.Object || binding.PhysicalPath.IsNested);
            var type = structured ? "json" : StoreType((identity ?? root.First()).PhysicalType, identity is not null);
            var autoIncrement = identity?.Descriptor.Generation == MappingGeneration.Provider && IsInteger(identity.PhysicalType);
            columns.Add(new ExpectedColumn(root.Key, type, Nullable: identity is null, Required: true,
                Critical: identity is not null || structured, Identity: identity is not null,
                AutoIncrement: autoIncrement, StoredGenerated: false));
        }

        var structuredRoot = plan.Mapping.Bindings.SingleOrDefault(static binding =>
            binding.Shape == MappingValueShape.Object && binding.LogicalPath.IsRoot);
        if (structuredRoot is null) return columns;
        foreach (var binding in plan.Mapping.Bindings.Where(binding =>
                     binding.Descriptor.Authority == MappingAuthority.Derived &&
                     binding.Shape == MappingValueShape.Scalar &&
                     binding.LogicalPath.Segments.Count == 1 &&
                     !rootNames.Contains(binding.LogicalPath.Leaf)))
        {
            columns.Add(new ExpectedColumn(binding.LogicalPath.Leaf, StoreType(binding.PhysicalType, false), Nullable: true,
                Required: false, Critical: false, Identity: false, AutoIncrement: false, StoredGenerated: true));
        }
        return columns;
    }

    private static bool TypeMatches(ColumnShape actual, string expectedStoreType)
    {
        var expected = expectedStoreType;
        string? expectedCharacterSet = null;
        string? expectedCollation = null;
        var characterSetMarker = expected.IndexOf(" CHARACTER SET ", StringComparison.OrdinalIgnoreCase);
        if (characterSetMarker >= 0)
        {
            var suffix = expected[(characterSetMarker + " CHARACTER SET ".Length)..];
            expected = expected[..characterSetMarker];
            var parts = suffix.Split(" COLLATE ", 2, StringSplitOptions.TrimEntries);
            expectedCharacterSet = parts[0];
            if (parts.Length == 2) expectedCollation = parts[1];
        }
        if (string.Equals(expected, "boolean", StringComparison.OrdinalIgnoreCase)) expected = "tinyint(1)";

        static string Normalize(string value) => string.Concat(value.Where(static character => !char.IsWhiteSpace(character)))
            .ToLowerInvariant();
        return Normalize(actual.ColumnType) == Normalize(expected) &&
               (expectedCharacterSet is null || string.Equals(actual.CharacterSet, expectedCharacterSet, StringComparison.OrdinalIgnoreCase)) &&
               (expectedCollation is null || string.Equals(actual.Collation, expectedCollation, StringComparison.OrdinalIgnoreCase));
    }

    private static string Describe(ColumnShape column) => column.CharacterSet is null
        ? column.ColumnType
        : $"{column.ColumnType} CHARACTER SET {column.CharacterSet} COLLATE {column.Collation ?? "<none>"}";

    private static bool DdlAllowed(MySqlRepositoryOptions options) =>
        options.SourcePlan.StorageLifecycle == StorageLifecycle.Managed &&
        options.SourcePlan.Access == DataSourceAccess.ReadWrite &&
        options.DdlPolicy == RelationalDdlPolicy.AutoCreate &&
        RelationalDdlGate.Allowed(options.AllowProductionDdl);

    private static SchemaMismatchException Mismatch<TEntity, TKey>(
        MySqlEntityPlan<TEntity, TKey> plan,
        MySqlRepositoryOptions options,
        IReadOnlyCollection<string> missing,
        IReadOnlyCollection<string> incompatible)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull => new(
            typeof(TEntity).FullName ?? typeof(TEntity).Name,
            plan.QualifiedTable,
            options.SchemaMatching.ToString(),
            missing.ToArray(),
            incompatible.ToArray(),
            DdlAllowed(options));

    private static string CreateSql<TEntity, TKey>(MySqlEntityPlan<TEntity, TKey> plan)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var definitions = new List<string>();
        var rootNames = plan.Mapping.Bindings.Select(static binding => binding.PhysicalPath.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var root in plan.Mapping.Bindings.GroupBy(static binding => binding.PhysicalPath.Name, StringComparer.Ordinal))
        {
            var identity = root.FirstOrDefault(binding => plan.Mapping.Identity.Parts.Any(part => part.Id == binding.Id));
            var structured = root.Any(binding => binding.Shape == MappingValueShape.Object || binding.PhysicalPath.IsNested);
            var type = structured ? "json" : StoreType((identity ?? root.First()).PhysicalType, identity is not null);
            var generated = identity?.Descriptor.Generation == MappingGeneration.Provider && IsInteger(identity.PhysicalType)
                ? " AUTO_INCREMENT"
                : string.Empty;
            definitions.Add($"{MySqlDialect.Quote(root.Key)} {type}{generated} {(identity is null ? "NULL" : "NOT NULL")}");
        }

        var structuredRoot = plan.Mapping.Bindings.SingleOrDefault(static binding =>
            binding.Shape == MappingValueShape.Object && binding.LogicalPath.IsRoot);
        if (structuredRoot is not null)
        {
            foreach (var binding in plan.Mapping.Bindings.Where(binding =>
                         binding.Descriptor.Authority == MappingAuthority.Derived &&
                         binding.Shape == MappingValueShape.Scalar &&
                         binding.LogicalPath.Segments.Count == 1 &&
                         !rootNames.Contains(binding.LogicalPath.Leaf)))
            {
                var json = $"JSON_UNQUOTE(JSON_EXTRACT({MySqlDialect.Quote(structuredRoot.PhysicalPath.Name)}, " +
                           $"'{MySqlDialect.JsonPath(binding.PhysicalPath.Segments)}'))";
                definitions.Add($"{MySqlDialect.Quote(binding.LogicalPath.Leaf)} {StoreType(binding.PhysicalType, false)} " +
                                $"GENERATED ALWAYS AS ({MySqlDialect.Cast(json, binding.PhysicalType)}) STORED");
            }
        }

        definitions.Add($"PRIMARY KEY ({string.Join(", ", plan.IdentityRoots.Select(MySqlDialect.Quote))})");
        return $"CREATE TABLE IF NOT EXISTS {plan.QualifiedTable} ({string.Join(", ", definitions)}) ENGINE={RequiredEngine}";
    }

    private static string StoreType(Type type, bool key)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        if (value.IsEnum) value = Enum.GetUnderlyingType(value);
        if (value == typeof(bool)) return "boolean";
        if (value == typeof(byte)) return "tinyint unsigned";
        if (value == typeof(sbyte)) return "tinyint";
        if (value == typeof(short)) return "smallint";
        if (value == typeof(ushort)) return "smallint unsigned";
        if (value == typeof(int)) return "int";
        if (value == typeof(uint)) return "int unsigned";
        if (value == typeof(long) || value == typeof(TimeSpan)) return "bigint";
        if (value == typeof(ulong)) return "bigint unsigned";
        if (value == typeof(float)) return "float";
        if (value == typeof(double)) return "double";
        if (value == typeof(decimal)) return "decimal(65,30)";
        if (value == typeof(Guid)) return "char(36)";
        if (value == typeof(DateTime)) return "datetime(6)";
        if (value == typeof(DateTimeOffset)) return "varchar(35)";
        if (value == typeof(DateOnly)) return "date";
        if (value == typeof(TimeOnly)) return "time(6)";
        if (value == typeof(byte[])) return "longblob";
        return key ? "varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin" : "longtext";
    }

    private static bool IsInteger(Type type)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        if (value.IsEnum) value = Enum.GetUnderlyingType(value);
        return value == typeof(byte) || value == typeof(sbyte) || value == typeof(short) || value == typeof(ushort) ||
               value == typeof(int) || value == typeof(uint) || value == typeof(long) || value == typeof(ulong);
    }

    private sealed record TableShape(string? Engine, IReadOnlyDictionary<string, ColumnShape> Columns);
    private sealed record ColumnShape(
        string Name,
        string DataType,
        string ColumnType,
        bool Nullable,
        string? CharacterSet,
        string? Collation,
        bool AutoIncrement,
        bool StoredGenerated,
        int? PrimaryOrdinal);
    private sealed record ExpectedColumn(
        string Name,
        string StoreType,
        bool Nullable,
        bool Required,
        bool Critical,
        bool Identity,
        bool AutoIncrement,
        bool StoredGenerated);
}
