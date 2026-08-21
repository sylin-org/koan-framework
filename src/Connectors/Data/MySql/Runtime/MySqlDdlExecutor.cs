using Koan.Data.Abstractions;
using Koan.Data.Relational.Orchestration;
using MySqlConnector;

namespace Koan.Data.Connector.MySql.Runtime;

/// <summary>
/// How MySQL spells a table, a column and a generated column. Every decision behind these words belongs to the
/// schema orchestrator; this speaks over a connection the repository has already opened, for one schema
/// operation.
///
/// <para>This is the one store that describes its own columns well enough to compare them, so it is the one that
/// judges them: a character set is invisible to a CLR type, and this adapter was catching that drift before the
/// seam existed.</para>
/// </summary>
internal sealed class MySqlDdlExecutor(MySqlConnection connection, MySqlDialect dialect) : IRelationalDdlExecutor
{
    private const string RequiredEngine = "InnoDB";

    public async Task<RelationalTableShape?> Describe(RelationalTableDefinition table, CancellationToken ct = default)
    {
        string? engine;
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText =
                "SELECT engine FROM information_schema.tables WHERE table_schema=@database AND table_name=@table LIMIT 1";
            probe.Parameters.AddWithValue("database", table.Schema);
            probe.Parameters.AddWithValue("table", table.Name);
            engine = Convert.ToString(await probe.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }
        if (engine is null) return null;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.column_name, c.column_type, c.is_nullable,
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
        command.Parameters.AddWithValue("database", table.Schema);
        command.Parameters.AddWithValue("table", table.Name);

        var columns = new Dictionary<string, RelationalColumnState?>(StringComparer.OrdinalIgnoreCase);
        var key = new List<(int Ordinal, string Name)>();
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var extra = Convert.ToString(reader.GetValue(5)) ?? string.Empty;
                var name = reader.GetString(0);
                columns[name] = new RelationalColumnState(
                    name,
                    Nullable: string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                    IsGenerated: extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
                    IsProjected: extra.Contains("stored generated", StringComparison.OrdinalIgnoreCase),
                    NativeType: Native(
                        reader.GetString(1),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4)));
                if (!reader.IsDBNull(6)) key.Add((Convert.ToInt32(reader.GetValue(6)), name));
            }
        }
        if (columns.Count == 0) return null;

        // The engine is a container fact no neutral column model can carry, and Koan's guarantees here - row
        // locking, transactional DDL rollback - are InnoDB's, not MySQL's in general.
        var incompatible = string.Equals(engine, RequiredEngine, StringComparison.OrdinalIgnoreCase)
            ? Array.Empty<string>()
            : [$"Table {table} must use the {RequiredEngine} engine; found {engine}."];
        return new RelationalTableShape(
            columns,
            key.OrderBy(static part => part.Ordinal).Select(static part => part.Name).ToArray(),
            incompatible);
    }

    /// <summary>
    /// Compares in MySQL's own vocabulary, because a CLR type cannot see a character set and this adapter has
    /// always caught that. Both sides are rendered the same way, so the comparison is symmetric; a collation the
    /// expectation does not name is one the mapping does not care about.
    /// </summary>
    public bool ColumnMatches(RelationalColumnDefinition expected, RelationalColumnState actual)
    {
        if (actual.NativeType is null) return false;
        var (wantType, wantCharacterSet, wantCollation) = Split(StoreType(expected));
        var (haveType, haveCharacterSet, haveCollation) = Split(actual.NativeType);
        // MySQL stores a boolean as tinyint(1) and reports it that way, so the expectation is read back the same.
        if (string.Equals(wantType, "boolean", StringComparison.OrdinalIgnoreCase)) wantType = "tinyint(1)";
        return string.Equals(Normalize(haveType), Normalize(wantType), StringComparison.Ordinal) &&
               (wantCharacterSet is null ||
                string.Equals(haveCharacterSet, wantCharacterSet, StringComparison.OrdinalIgnoreCase)) &&
               (wantCollation is null ||
                string.Equals(haveCollation, wantCollation, StringComparison.OrdinalIgnoreCase)) &&
               // MySQL constrains its key and nothing else, which is what Create writes and so what a table
               // Koan owns must hold.
               actual.Nullable != expected.IsIdentity;
    }

    public Task Create(RelationalTableDefinition table, CancellationToken ct = default)
    {
        var definitions = table.Columns.Select(Definition).ToList();
        definitions.Add($"PRIMARY KEY ({string.Join(", ", table.Identity.Select(MySqlDialect.Quote))})");
        return Execute(
            $"CREATE TABLE IF NOT EXISTS {Qualify(table)} " +
            $"({string.Join(", ", definitions)}) ENGINE={RequiredEngine}",
            ct);
    }

    public Task AddColumn(RelationalTableDefinition table, RelationalColumnDefinition column, CancellationToken ct = default) =>
        Execute($"ALTER TABLE {Qualify(table)} ADD COLUMN {Definition(column)}", ct);

    private string Definition(RelationalColumnDefinition column)
    {
        if (column.IsProjected)
        {
            var source = column.ProjectedFrom
                ?? throw new InvalidOperationException(
                    $"Projected column '{column.Name}' carries no source path to compute from.");
            // The generated expression is the dialect's own read, so the column holds exactly what a query
            // computing the same value would produce.
            return $"{MySqlDialect.Quote(column.Name)} {StoreType(column)} " +
                   $"GENERATED ALWAYS AS ({dialect.Read(source, MappingValueShape.Scalar, column.ClrType)}) STORED";
        }

        var generated = column.IsIdentity && column.IsGenerated && IsInteger(column.ClrType)
            ? " AUTO_INCREMENT"
            : string.Empty;
        return $"{MySqlDialect.Quote(column.Name)} {StoreType(column)}{generated} " +
               (column.IsIdentity ? "NOT NULL" : "NULL");
    }

    private async Task Execute(string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string Qualify(RelationalTableDefinition table) =>
        $"{MySqlDialect.Quote(table.Schema)}.{MySqlDialect.Quote(table.Name)}";

    private static string Native(string columnType, string? characterSet, string? collation) =>
        characterSet is null
            ? columnType
            : $"{columnType} CHARACTER SET {characterSet} COLLATE {collation ?? "<none>"}";

    private static (string Type, string? CharacterSet, string? Collation) Split(string spelling)
    {
        const string marker = " CHARACTER SET ";
        var at = spelling.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return (spelling, null, null);
        var suffix = spelling[(at + marker.Length)..].Split(" COLLATE ", 2, StringSplitOptions.TrimEntries);
        return (spelling[..at], suffix[0], suffix.Length == 2 ? suffix[1] : null);
    }

    private static string Normalize(string value) =>
        string.Concat(value.Where(static character => !char.IsWhiteSpace(character))).ToLowerInvariant();

    private static string StoreType(RelationalColumnDefinition column)
    {
        if (column.Shape == RelationalStorageShape.Structured) return "json";
        var value = Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType;
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
        // A key has to fit an index, and it is compared byte for byte, so its collation is part of the contract.
        return column.IsIdentity ? "varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin" : "longtext";
    }

    private static bool IsInteger(Type type)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        if (value.IsEnum) value = Enum.GetUnderlyingType(value);
        return value == typeof(byte) || value == typeof(sbyte) || value == typeof(short) || value == typeof(ushort) ||
               value == typeof(int) || value == typeof(uint) || value == typeof(long) || value == typeof(ulong);
    }
}
