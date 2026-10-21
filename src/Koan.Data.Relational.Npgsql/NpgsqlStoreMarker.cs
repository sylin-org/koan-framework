using Koan.Data.Abstractions;
using Npgsql;

namespace Koan.Data.Relational.Npgsql;

/// <summary>
/// The store marker of a PostgreSQL route (DATA-0148): one row of the reserved <c>__koan_store</c> table in the schema
/// the route writes its tables to, so one database under two schemas holds two markers, as it holds two sets of tables.
/// </summary>
public static class NpgsqlStoreMarker
{
    /// <summary>
    /// Reads the marker id. With <paramref name="create"/> the schema, table and row are written first if missing; the
    /// row is keyed by a fixed slot, so concurrent writers agree on the first id. Without it, a missing table reads as
    /// <see langword="null"/>.
    /// </summary>
    public static async ValueTask<string?> Read(string connection, string schema, bool create, CancellationToken ct = default)
    {
        await using var session = new NpgsqlConnection(connection);
        await session.OpenAsync(ct).ConfigureAwait(false);
        var table = $"{Quote(schema)}.{Quote(StoreMarker.Name)}";
        if (create)
        {
            try
            {
                await using var ddl = new NpgsqlCommand(
                    $"CREATE SCHEMA IF NOT EXISTS {Quote(schema)}; " +
                    $"CREATE TABLE IF NOT EXISTS {table} (slot integer PRIMARY KEY CHECK (slot = 1), id text NOT NULL)",
                    session);
                await ddl.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UniqueViolation
                                                      or PostgresErrorCodes.DuplicateTable
                                                      or PostgresErrorCodes.DuplicateSchema)
            {
                // Another writer created it between the existence check and the create.
            }

            await using var insert = new NpgsqlCommand(
                $"INSERT INTO {table} (slot, id) VALUES (1, @id) ON CONFLICT (slot) DO NOTHING", session);
            insert.Parameters.AddWithValue("id", StoreMarker.NewId());
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await using var exists = new NpgsqlCommand(
                "SELECT 1 FROM information_schema.tables WHERE table_schema = @schema AND table_name = @name", session);
            exists.Parameters.AddWithValue("schema", schema);
            exists.Parameters.AddWithValue("name", StoreMarker.Name);
            if (await exists.ExecuteScalarAsync(ct).ConfigureAwait(false) is null) return null;
        }

        await using var read = new NpgsqlCommand($"SELECT id FROM {table} WHERE slot = 1", session);
        return await read.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
