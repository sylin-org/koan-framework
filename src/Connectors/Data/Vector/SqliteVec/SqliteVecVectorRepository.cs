using Newtonsoft.Json;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Naming;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Vector.Connector.SqliteVec;

/// <summary>
/// Durable in-process vector repository over sqlite-vec. Each (entity, partition) maps to a <c>vec0</c>
/// virtual table in the configured SQLite file; vectors are stored as packed float32 BLOBs and queried with
/// <c>WHERE embedding MATCH ? ORDER BY distance</c>. A single connection (with vec0 loaded) is held for the
/// repository lifetime and access is serialized — correct and simple for the embedded floor.
/// </summary>
internal sealed class SqliteVecVectorRepository<TEntity, TKey>
    : IVectorSearchRepository<TEntity, TKey>, IDescribesCapabilities, IDisposable
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly SqliteVecAdapterFactory _factory;
    private readonly IServiceProvider _sp;
    private readonly SqliteVecOptions _options;
    private readonly string _source;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly HashSet<string> _ensured = new(StringComparer.Ordinal);
    private SqliteConnection? _conn;

    public SqliteVecVectorRepository(
        SqliteVecAdapterFactory factory,
        IServiceProvider sp,
        SqliteVecOptions options,
        string source)
    {
        _factory = factory;
        _sp = sp;
        _options = options;
        _source = source;
    }

    public void Describe(ICapabilities caps) => caps
        .Add(VectorCaps.Knn)
        .Add(VectorCaps.BulkUpsert).Add(VectorCaps.BulkDelete)
        .Add(VectorCaps.AtomicBatch)        // real SQL transactions
        .Add(VectorCaps.ScoreNormalization) // cosine distance -> [0,1] similarity
        .Add(VectorCaps.DynamicCollections); // one vec0 table per (entity, partition)

    private SqliteConnection Connection()
    {
        if (_conn is not null) return _conn;
        SqliteVecRoute.PrepareFileSystem(_options.ConnectionString);
        var conn = new SqliteConnection(_options.ConnectionString);
        conn.Open();
        Vec0Native.Load(conn);
        _conn = conn;
        return conn;
    }

    private string Table()
    {
        var name = VectorAdapterNaming.GetOrCompute<TEntity>(_sp, _factory, _source);
        return "vec_" + Sanitize(name);
    }

    private void EnsureTable(SqliteConnection conn, string table, int dim)
    {
        if (_ensured.Contains(table)) return;
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"CREATE VIRTUAL TABLE IF NOT EXISTS \"{table}\" USING vec0(" +
            $"id TEXT PRIMARY KEY, embedding float[{dim}] distance_metric={Metric()}, +metadata TEXT)";
        cmd.ExecuteNonQuery();
        _ensured.Add(table);
    }

    private string Metric() => _options.DistanceMetric.Trim().ToLowerInvariant() switch
    {
        "l2" => "L2",
        "l1" => "L1",
        "cosine" => "cosine",
        _ => throw new InvalidOperationException(
            $"Unsupported sqlite-vec distance metric '{_options.DistanceMetric}'. Use cosine, l2, or l1."),
    };

    public async Task Upsert(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length == 0) throw new ArgumentException("Embedding must contain values.", nameof(embedding));
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var conn = Connection();
            var table = Table();
            EnsureTable(conn, table, embedding.Length);
            UpsertCore(conn, table, Key(id), embedding, metadata);
        }
        finally { _lock.Release(); }
    }

    public async Task<int> UpsertMany(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = items.ToList();
        if (list.Count == 0) return 0;
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var conn = Connection();
            var table = Table();
            EnsureTable(conn, table, list[0].Embedding.Length);
            using var tx = conn.BeginTransaction();
            foreach (var (id, emb, meta) in list)
            {
                if (emb.Length != list[0].Embedding.Length)
                    throw new InvalidOperationException($"Embedding dimension mismatch: expected {list[0].Embedding.Length}, got {emb.Length}.");
                UpsertCore(conn, table, Key(id), emb, meta, tx);
            }
            tx.Commit();
            return list.Count;
        }
        finally { _lock.Release(); }
    }

    private static void UpsertCore(SqliteConnection conn, string table, string id, float[] embedding, object? metadata, SqliteTransaction? tx = null)
    {
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = $"DELETE FROM \"{table}\" WHERE id = $id";
            del.Parameters.AddWithValue("$id", id);
            del.ExecuteNonQuery();
        }
        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = $"INSERT INTO \"{table}\"(id, embedding, metadata) VALUES ($id, $emb, $meta)";
        ins.Parameters.AddWithValue("$id", id);
        ins.Parameters.AddWithValue("$emb", ToBlob(embedding));
        ins.Parameters.AddWithValue("$meta", (object?)SerializeMeta(metadata) ?? DBNull.Value);
        ins.ExecuteNonQuery();
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var conn = Connection();
            var table = Table();
            if (!TableExists(conn, table)) return false;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM \"{table}\" WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", Key(id));
            return cmd.ExecuteNonQuery() > 0;
        }
        finally { _lock.Release(); }
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        var list = ids.ToList();
        if (list.Count == 0) return 0;
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var conn = Connection();
            var table = Table();
            if (!TableExists(conn, table)) return 0;
            using var tx = conn.BeginTransaction();
            var count = 0;
            foreach (var id in list)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM \"{table}\" WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", Key(id));
                count += cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return count;
        }
        finally { _lock.Release(); }
    }

    public async Task<float[]?> GetEmbedding(TKey id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var conn = Connection();
            var table = Table();
            if (!TableExists(conn, table)) return null;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT vec_to_json(embedding) FROM \"{table}\" WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", Key(id));
            var json = cmd.ExecuteScalar() as string;
            return json is null ? null : JsonConvert.DeserializeObject<float[]>(json);
        }
        finally { _lock.Release(); }
    }

    public async Task<VectorQueryResult<TKey>> Search(VectorQueryOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var topK = options.TopK ?? 10;
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var conn = Connection();
            var table = Table();
            if (!TableExists(conn, table))
                return new VectorQueryResult<TKey>(Array.Empty<VectorMatch<TKey>>(), null, VectorTotalKind.Exact);

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"SELECT id, distance, metadata FROM \"{table}\" " +
                "WHERE embedding MATCH $q ORDER BY distance LIMIT $k";
            cmd.Parameters.AddWithValue("$q", ToBlob(options.Query));
            cmd.Parameters.AddWithValue("$k", topK);

            var matches = new List<VectorMatch<TKey>>(topK);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = ParseKey(reader.GetString(0));
                var distance = reader.GetDouble(1);
                object? meta = reader.IsDBNull(2) ? null : reader.GetString(2);
                // cosine distance in [0,2] -> similarity in [-1,1]; for L2/L1 fall back to negative distance.
                var score = string.Equals(_options.DistanceMetric, "cosine", StringComparison.OrdinalIgnoreCase)
                    ? 1.0 - distance
                    : -distance;
                matches.Add(new VectorMatch<TKey>(id, score, meta));
            }
            return new VectorQueryResult<TKey>(matches, null, VectorTotalKind.Exact);
        }
        finally { _lock.Release(); }
    }

    public Task VectorEnsureCreated(CancellationToken ct = default) => Task.CompletedTask;

    public async Task Flush(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var conn = Connection();
            var table = Table();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP TABLE IF EXISTS \"{table}\"";
            cmd.ExecuteNonQuery();
            _ensured.Remove(table);
        }
        finally { _lock.Release(); }
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n";
        cmd.Parameters.AddWithValue("$n", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static byte[] ToBlob(float[] v)
    {
        var bytes = new byte[v.Length * sizeof(float)];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length); // little-endian float32 on x64/arm64 = sqlite-vec's format
        return bytes;
    }

    private static string? SerializeMeta(object? metadata)
        => metadata is null ? null : JsonConvert.SerializeObject(metadata);

    private static string Key(TKey id) => id?.ToString() ?? throw new ArgumentNullException(nameof(id));

    private static TKey ParseKey(string raw)
        => typeof(TKey) == typeof(string) ? (TKey)(object)raw : (TKey)Convert.ChangeType(raw, typeof(TKey))!;

    private static string Sanitize(string name)
    {
        Span<char> buf = stackalloc char[name.Length];
        for (var i = 0; i < name.Length; i++)
            buf[i] = char.IsLetterOrDigit(name[i]) ? name[i] : '_';
        return new string(buf);
    }

    public void Dispose()
    {
        _conn?.Dispose();
        _lock.Dispose();
    }
}
