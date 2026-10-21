using Npgsql;

namespace Koan.Data.Relational.Npgsql;

/// <summary>
/// Whether two PostgreSQL routes open different stores, asked of the servers rather than of the connection text (NK-305).
/// An advisory lock is keyed by database: a lock one session holds is refused to every other session on that database
/// and never conflicts with a session on another database, whatever host name, address or port reached it. Each
/// session takes a transaction-scoped lock inside its own transaction, so the probe needs no privilege, survives
/// transaction-mode pooling, and writes nothing; both transactions roll back.
/// </summary>
public static class NpgsqlStoreProof
{
    /// <summary>
    /// True only when proven: the two routes reach different databases, or one database under different schemas.
    /// A lock already held by someone else, or any failure, is not proof.
    /// </summary>
    public static async ValueTask<bool> Distinct(
        string connection,
        string schema,
        string otherConnection,
        string otherSchema,
        CancellationToken ct = default)
    {
        await using var first = new NpgsqlConnection(connection);
        await first.OpenAsync(ct).ConfigureAwait(false);
        await using var second = new NpgsqlConnection(otherConnection);
        await second.OpenAsync(ct).ConfigureAwait(false);
        await using var held = await first.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var probe = await second.BeginTransactionAsync(ct).ConfigureAwait(false);
        var key = Random.Shared.NextInt64();
        if (!await TryLock(first, held, key, ct).ConfigureAwait(false)) return false;
        if (await TryLock(second, probe, key, ct).ConfigureAwait(false)) return true;
        // One database: the tables are qualified by schema, so different schemas are different containers.
        return !string.Equals(schema, otherSchema, StringComparison.Ordinal);
    }

    private static async Task<bool> TryLock(NpgsqlConnection connection, NpgsqlTransaction transaction, long key, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@key)", connection, transaction);
        command.Parameters.AddWithValue("key", key);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;
    }
}
