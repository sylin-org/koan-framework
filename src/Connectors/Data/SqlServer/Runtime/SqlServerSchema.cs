using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Relational.Orchestration;
using Microsoft.Data.SqlClient;

namespace Koan.Data.Connector.SqlServer.Runtime;

internal static class SqlServerSchema
{
    public static async Task Provision<TEntity, TKey>(
        SqlConnection connection,
        SqlServerEntityPlan<TEntity, TKey> plan,
        SqlServerRepositoryOptions options,
        CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        if (await Exists(connection, plan.Schema, plan.Table, ct).ConfigureAwait(false)) return;
        var canCreate = options.SourcePlan.StorageLifecycle == StorageLifecycle.Managed &&
                        options.SourcePlan.Access == DataSourceAccess.ReadWrite &&
                        options.DdlPolicy == RelationalDdlPolicy.AutoCreate;
        if (!canCreate)
            throw new SchemaMismatchException(typeof(TEntity).FullName ?? typeof(TEntity).Name, plan.QualifiedTable,
                options.SourcePlan.StorageLifecycle.ToString(), ["table"], [], ddlAllowed: false);

        // Lifecycle and policy say whether this source may be provisioned at all; they say nothing about
        // where. Automatic DDL issues CREATE against whatever the connection string resolves to, which in
        // production is live data, so the environment is a separate question with a separate answer
        // (DATA-0119, ARCH-0128). The consent value was carried this far all along and never read.
        if (!RelationalDdlGate.Allowed(options.AllowProductionDdl))
            throw new InvalidOperationException(
                $"SQL Server cannot provision '{plan.QualifiedTable}'. {RelationalDdlGate.Refusal}");

        await using var command = connection.CreateCommand();
        command.CommandText = CreateSql(plan);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public static async Task Validate<TEntity, TKey>(
        SqlConnection connection,
        SqlServerEntityPlan<TEntity, TKey> plan,
        SqlServerRepositoryOptions options,
        CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        if (!await Exists(connection, plan.Schema, plan.Table, ct).ConfigureAwait(false))
            throw new SchemaMismatchException(typeof(TEntity).FullName ?? typeof(TEntity).Name, plan.QualifiedTable,
                options.SourcePlan.StorageLifecycle.ToString(), ["table"], [],
                options.SourcePlan.StorageLifecycle == StorageLifecycle.Managed);
    }

    public static async Task<bool> Exists(SqlConnection connection, string schema, string table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name=@schema AND t.name=@table";
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private static string CreateSql<TEntity, TKey>(SqlServerEntityPlan<TEntity, TKey> plan)
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
            var type = structured ? "nvarchar(max)" : StoreType((identity ?? root.First()).PhysicalType, identity is not null);
            var generated = identity?.Descriptor.Generation == MappingGeneration.Provider && IsNumeric(identity.PhysicalType)
                ? " IDENTITY(1,1)"
                : string.Empty;
            definitions.Add($"{SqlServerDialect.Quote(root.Key)} {type}{generated} {(identity is null ? "NULL" : "NOT NULL")}");
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
                var json = $"JSON_VALUE({SqlServerDialect.Quote(structuredRoot.PhysicalPath.Name)}, '{SqlServerDialect.JsonPath(binding.PhysicalPath.Segments)}')";
                definitions.Add($"{SqlServerDialect.Quote(binding.LogicalPath.Leaf)} AS {Computed(json, binding.PhysicalType)} PERSISTED");
            }
        }

        definitions.Add($"PRIMARY KEY NONCLUSTERED ({string.Join(", ", plan.IdentityRoots.Select(SqlServerDialect.Quote))})");
        return $"IF SCHEMA_ID(N'{Literal(plan.Schema)}') IS NULL EXEC(N'CREATE SCHEMA {SqlServerDialect.Quote(plan.Schema)}'); " +
               $"IF OBJECT_ID(N'{Literal(plan.Schema)}.{Literal(plan.Table)}', N'U') IS NULL CREATE TABLE {plan.QualifiedTable} ({string.Join(", ", definitions)});";
    }

    private static string Computed(string expression, Type type)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        if (value.IsEnum) value = Enum.GetUnderlyingType(value);
        if (value == typeof(bool)) return $"TRY_CONVERT(bit, {expression})";
        if (IsNumeric(value) || value == typeof(TimeSpan)) return $"TRY_CONVERT(bigint, {expression})";
        if (value == typeof(float) || value == typeof(double) || value == typeof(decimal))
            return $"TRY_CONVERT(decimal(38,10), {expression})";
        return expression;
    }

    private static string StoreType(Type type, bool key)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        if (value.IsEnum) value = Enum.GetUnderlyingType(value);
        if (value == typeof(bool)) return "bit";
        if (value == typeof(byte) || value == typeof(sbyte) || value == typeof(short)) return "smallint";
        if (value == typeof(int) || value == typeof(ushort)) return "int";
        if (value == typeof(long) || value == typeof(uint) || value == typeof(TimeSpan)) return "bigint";
        if (value == typeof(float)) return "real";
        if (value == typeof(double)) return "float";
        if (value == typeof(decimal)) return "decimal(38,10)";
        if (value == typeof(Guid)) return "uniqueidentifier";
        if (value == typeof(DateTime)) return "datetime2";
        if (value == typeof(DateTimeOffset)) return "datetimeoffset";
        if (value == typeof(DateOnly)) return "date";
        if (value == typeof(TimeOnly)) return "time";
        if (value == typeof(byte[])) return "varbinary(max)";
        return key ? "nvarchar(450)" : "nvarchar(max)";
    }

    private static bool IsNumeric(Type type)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        if (value.IsEnum) value = Enum.GetUnderlyingType(value);
        return value == typeof(byte) || value == typeof(sbyte) || value == typeof(short) || value == typeof(ushort) ||
               value == typeof(int) || value == typeof(uint) || value == typeof(long) || value == typeof(ulong);
    }

    private static string Literal(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
