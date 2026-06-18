using Microsoft.Data.SqlClient;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.SqlServer;

internal sealed class MsSqlDdlExecutor : IRelationalDdlExecutor
{
    private readonly SqlConnection _conn;
    public MsSqlDdlExecutor(SqlConnection conn) => _conn = conn;

    public bool TableExists(string schema, string table)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE t.name=@t AND s.name=@s";
        cmd.Parameters.Add(new SqlParameter("@t", table));
        cmd.Parameters.Add(new SqlParameter("@s", schema));
        try { var o = cmd.ExecuteScalar(); return o is not null; } catch { return false; }
    }

    public bool ColumnExists(string schema, string table, string column)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT 1 FROM sys.columns c
JOIN sys.tables t ON c.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.name = @t AND s.name = @s AND c.name = @c";
        cmd.Parameters.Add(new SqlParameter("@t", table));
        cmd.Parameters.Add(new SqlParameter("@s", schema));
        cmd.Parameters.Add(new SqlParameter("@c", column));
        try { var o = cmd.ExecuteScalar(); return o is not null; } catch { return false; }
    }

    public void CreateTableIdJson(string schema, string table, string idColumn = "Id", string jsonColumn = "Json")
    {
        using var cmd = _conn.CreateCommand();
        // Inline UNNAMED primary key: SQL Server auto-generates a constraint name that always fits sysname (128).
        // An explicit CONSTRAINT [PK_<table>_<id>] overflows 128 when the table name (already up to its 128-byte
        // MaxIdentifierBytes limit under a long entity namespace + partition suffix) is prefixed/suffixed — the
        // failure SQL Server surfaces as a later "Invalid object name" because the CREATE was silently lost.
        cmd.CommandText = $@"IF OBJECT_ID(N'[{schema}].[{table}]', N'U') IS NULL
BEGIN
    CREATE TABLE [{schema}].[{table}] (
        [{idColumn}] NVARCHAR(128) NOT NULL PRIMARY KEY,
        [{jsonColumn}] NVARCHAR(MAX) NOT NULL
    );
END";
        try { cmd.ExecuteNonQuery(); }
        catch when (TableExists(schema, table)) { /* created concurrently — idempotent, not an error */ }
    }

    // Create table with provided columns (Id, Json already included in columns list expected by orchestrator)
    public void CreateTableWithColumns(string schema, string table, List<(string Name, Type ClrType, bool Nullable, bool IsComputed, string? JsonPath, bool IsIndexed)> columns)
    {
        using var cmd = _conn.CreateCommand();
        // Build column definitions. Expect Id and Json to be present as first two columns.
        var defs = new System.Text.StringBuilder();
        foreach (var col in columns)
        {
            if (defs.Length > 0) defs.AppendLine(",");
            if (string.Equals(col.Name, "Id", StringComparison.OrdinalIgnoreCase))
            {
                // Inline UNNAMED primary key (see CreateTableIdJson): an explicit CONSTRAINT [PK_<table>_Id]
                // overflows sysname's 128-byte limit when the table name is already at its MaxIdentifierBytes
                // cap, and the failed CREATE was silently swallowed → later "Invalid object name".
                defs.Append($"[{col.Name}] NVARCHAR(128) NOT NULL PRIMARY KEY");
                continue;
            }
            if (string.Equals(col.Name, "Json", StringComparison.OrdinalIgnoreCase))
            {
                defs.Append($"[{col.Name}] NVARCHAR(MAX) NOT NULL");
                continue;
            }
            if (col.IsComputed && !string.IsNullOrEmpty(col.JsonPath))
            {
                // Use PERSISTED since SQL Server supports persisted computed columns
                defs.Append($"[{col.Name}] AS JSON_VALUE([Json], '{col.JsonPath}') PERSISTED");
            }
            else
            {
                var sqlType = MapType(col.ClrType);
                var nullSql = col.Nullable ? " NULL" : " NOT NULL";
                defs.Append($"[{col.Name}] {sqlType}{nullSql}");
            }
        }

        cmd.CommandText = $@"IF OBJECT_ID(N'[{schema}].[{table}]', N'U') IS NULL
BEGIN
    CREATE TABLE [{schema}].[{table}] (
{defs}
    );
END";
        // Surface a genuine CREATE TABLE failure (an idempotent concurrent create is tolerated). Previously this
        // swallowed ALL errors, hiding schema-creation failures behind a downstream "Invalid object name".
        try { cmd.ExecuteNonQuery(); }
        catch when (TableExists(schema, table)) { /* created concurrently — idempotent, not an error */ }

        // Create indexes for any indexed columns
        for (int i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            if (c.IsIndexed)
            {
                var ixName = $"IX_{table}_{c.Name}";
                CreateIndex(schema, table, ixName, new[] { c.Name }, unique: false);
            }
        }
    }

    public void AddComputedColumnFromJson(string schema, string table, string column, string jsonPath, bool persisted)
    {
        using var cmd = _conn.CreateCommand();
        var persist = persisted ? " PERSISTED" : "";
        cmd.CommandText = $"ALTER TABLE [{schema}].[{table}] ADD [{column}] AS JSON_VALUE([Json], '{jsonPath}'){persist}";
        try { cmd.ExecuteNonQuery(); } catch { }
    }

    public void AddPhysicalColumn(string schema, string table, string column, Type clrType, bool nullable)
    {
        using var cmd = _conn.CreateCommand();
        var sqlType = MapType(clrType);
        var nullSql = nullable ? " NULL" : " NOT NULL";
        cmd.CommandText = $"ALTER TABLE [{schema}].[{table}] ADD [{column}] {sqlType}{nullSql}";
        try { cmd.ExecuteNonQuery(); } catch { }
    }

    public void CreateIndex(string schema, string table, string indexName, IReadOnlyList<string> columns, bool unique)
    {
        indexName = BoundIdentifier(indexName);
        using var cmd = _conn.CreateCommand();
        var uq = unique ? "UNIQUE " : "";
        var cols = string.Join(", ", columns.Select(c => $"[{c}]"));
        cmd.CommandText = $@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{indexName}' AND object_id = OBJECT_ID(N'[{schema}].[{table}]'))
CREATE {uq}INDEX [{indexName}] ON [{schema}].[{table}] ({cols});";
        try { cmd.ExecuteNonQuery(); } catch { }
    }

    // Keep a generated identifier within SQL Server's 128-byte sysname limit, preserving uniqueness by
    // appending a short stable hash of the full name when truncation is needed.
    private static string BoundIdentifier(string name)
    {
        const int Max = 128;
        if (name.Length <= Max) return name;
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name)))[..8];
        return string.Concat(name.AsSpan(0, Max - hash.Length - 1), "_", hash);
    }

    private static string MapType(Type clr)
    {
        clr = Nullable.GetUnderlyingType(clr) ?? clr;
        if (clr == typeof(int)) return "INT";
        if (clr == typeof(long)) return "BIGINT";
        if (clr == typeof(short)) return "SMALLINT";
        if (clr == typeof(bool)) return "BIT";
        if (clr == typeof(DateTime)) return "DATETIME2";
        if (clr == typeof(decimal)) return "DECIMAL(18,2)";
        if (clr == typeof(double)) return "FLOAT";
        if (clr == typeof(Guid)) return "UNIQUEIDENTIFIER";
        return "NVARCHAR(256)";
    }
}
