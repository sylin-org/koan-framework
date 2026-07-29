using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Relational.Orchestration;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal static class SqliteSchema
{
    internal static async Task Provision<TEntity, TKey>(
        SqliteRoute route,
        SqliteConnections connections,
        SqliteEntityPlan<TEntity, TKey> plan,
        CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        if (route.Options.DdlPolicy != RelationalDdlPolicy.AutoCreate)
            throw new InvalidOperationException(
                $"SQLite cannot provision '{plan.Table}' because DdlPolicy is {route.Options.DdlPolicy}.");
        if (Koan.Core.KoanEnv.IsProduction && !route.Options.AllowProductionDdl)
            throw new InvalidOperationException(
                $"SQLite production DDL is disabled for '{plan.Table}'. Enable it only for a Koan-owned store.");

        connections.PrepareManaged(route.ConnectionString);
        await using var connection = connections.Create(route.ConnectionString, route.Source);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = CreateTable(plan);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    internal static async Task Validate<TEntity, TKey>(
        SqliteRoute route,
        SqliteConnections connections,
        SqliteEntityPlan<TEntity, TKey> plan,
        CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        await using var connection = connections.Create(
            route.ConnectionString,
            route.Source,
            nonCreating: route.Policy.StorageLifecycle == Koan.Data.Abstractions.Sources.StorageLifecycle.External);
        try { await connection.OpenAsync(ct).ConfigureAwait(false); }
        catch (SqliteException) when (route.Policy.StorageLifecycle == Koan.Data.Abstractions.Sources.StorageLifecycle.External)
        {
            throw new SchemaMismatchException(
                typeof(TEntity).FullName ?? typeof(TEntity).Name,
                plan.Table,
                route.Options.SchemaMatching.ToString(),
                ["The external SQLite database is absent or cannot be opened without creation."],
                [],
                ddlAllowed: false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({SqliteDialect.Quote(plan.Table)})";
        var columns = new Dictionary<string, Column>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                columns[reader.GetString(1)] = new Column(reader.GetString(2), reader.GetInt64(5) > 0);
        }

        if (columns.Count == 0)
            throw Mismatch<TEntity, TKey>(route, plan, [$"Container '{plan.Table}' does not exist."]);

        var missing = plan.Mapping.Bindings
            .Select(static binding => binding.PhysicalPath.Name)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !columns.ContainsKey(name))
            .Select(name => $"Missing physical value '{name}'.")
            .ToArray();
        if (missing.Length != 0) throw Mismatch<TEntity, TKey>(route, plan, missing);

        if (route.Options.SchemaMatching == RelationalSchemaMatchingMode.Strict)
        {
            var expectedKeys = plan.IdentityRoots.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actualKeys = columns.Where(static pair => pair.Value.PrimaryKey)
                .Select(static pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!expectedKeys.SetEquals(actualKeys))
                throw Mismatch<TEntity, TKey>(route, plan,
                    [$"Primary key differs: expected [{string.Join(", ", expectedKeys)}], found [{string.Join(", ", actualKeys)}]."]);
        }
    }

    private static string CreateTable<TEntity, TKey>(SqliteEntityPlan<TEntity, TKey> plan)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var identity = plan.Mapping.Identity.Parts.Select(static part => part.Id).ToHashSet(StringComparer.Ordinal);
        var groups = plan.Mapping.Bindings.GroupBy(static binding => binding.PhysicalPath.Name, StringComparer.Ordinal).ToArray();
        var singleGenerated = plan.Mapping.Identity.IsGenerated && plan.Mapping.Identity.Parts.Count == 1;
        var definitions = new List<string>(groups.Length + 1);
        foreach (var group in groups)
        {
            var bindings = group.ToArray();
            var key = bindings.All(binding => identity.Contains(binding.Id));
            if (key && singleGenerated)
            {
                definitions.Add($"{SqliteDialect.Quote(group.Key)} INTEGER PRIMARY KEY AUTOINCREMENT");
                continue;
            }
            var structured = bindings.Any(static binding =>
                binding.Shape == MappingValueShape.Object || binding.PhysicalPath.IsNested);
            var type = structured ? "TEXT" : StoreType(bindings[0].PhysicalType);
            definitions.Add($"{SqliteDialect.Quote(group.Key)} {type}{(key ? " NOT NULL" : string.Empty)}");
        }
        if (!singleGenerated)
            definitions.Add("PRIMARY KEY (" + string.Join(", ", plan.IdentityRoots.Select(SqliteDialect.Quote)) + ")");
        return $"CREATE TABLE IF NOT EXISTS {plan.QualifiedTable} ({string.Join(", ", definitions)})";
    }

    private static string StoreType(Type value)
    {
        var type = Nullable.GetUnderlyingType(value) ?? value;
        if (type == typeof(byte[])) return "BLOB";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "REAL";
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
            type == typeof(bool) || type == typeof(TimeSpan)) return "INTEGER";
        return "TEXT";
    }

    private static SchemaMismatchException Mismatch<TEntity, TKey>(
        SqliteRoute route,
        SqliteEntityPlan<TEntity, TKey> plan,
        IReadOnlyList<string> failures)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull => new(
            typeof(TEntity).FullName ?? typeof(TEntity).Name,
            plan.Table,
            route.Options.SchemaMatching.ToString(),
            failures.ToArray(),
            [],
            route.Policy.UsesLegacyProvisioningReadiness);

    private sealed record Column(string Type, bool PrimaryKey);
}
