using System.ComponentModel;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Naming;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Vector.Connector.SqliteVec;

internal sealed class SqliteVecRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IDisposable,
    IAsyncDisposable
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;
    private readonly SqliteVecAdapterFactory _factory;
    private readonly VectorSpacePlan _plan;
    private readonly SqliteVecRoute _route;
    private readonly SqliteVecOptions _options;
    private readonly SqliteVecNative _native;
    private readonly string _connectionString;
    private readonly Lazy<Task<SqliteConnection>>? _memoryKeeper;
    private readonly SemaphoreSlim _shapeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _readyShapes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _poolGroups = new(StringComparer.Ordinal);
    private int _disposed;

    internal SqliteVecRepository(
        IServiceProvider services,
        SqliteVecAdapterFactory factory,
        VectorSpacePlan plan,
        SqliteVecRoute route,
        SqliteVecOptions options,
        SqliteVecNative native)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        _services = services;
        _factory = factory;
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _native = native ?? throw new ArgumentNullException(nameof(native));
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("SqliteVec float32 storage requires a little-endian platform.");

        var parsed = Parse(route.ConnectionString);
        if (IsMemory(parsed))
        {
            parsed.DataSource = $"koan-sqlitevec-{Guid.NewGuid():N}";
            parsed.Mode = SqliteOpenMode.Memory;
            parsed.Cache = SqliteCacheMode.Shared;
            parsed.Pooling = false;
            _connectionString = parsed.ToString();
            _memoryKeeper = new Lazy<Task<SqliteConnection>>(
                OpenMemoryKeeper,
                LazyThreadSafetyMode.ExecutionAndPublication);
        }
        else
        {
            _connectionString = parsed.ToString();
        }
    }

    public void Describe(ICapabilities capabilities) => capabilities
        .Add(VectorCaps.Knn)
        .Add(VectorCaps.BulkUpsert)
        .Add(VectorCaps.BulkDelete)
        .Add(VectorCaps.AtomicBatch)
        .Add(VectorCaps.ScoreNormalization)
        .Add(VectorCaps.DynamicCollections)
        .Add(VectorCaps.ScopeIsolation);

    public async Task Save(VectorPoint<TKey> point, VectorScope scope, CancellationToken ct = default)
    {
        var prepared = Prepare(point);
        var table = Table();
        await EnsureShape(table, ct).ConfigureAwait(false);
        await using var connection = await Open(create: false, ct).ConfigureAwait(false)
            ?? throw MissingSource();
        using var transaction = connection.BeginTransaction();
        await Upsert(connection, transaction, table, prepared, Scope(scope), ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<BatchResult<TKey>> Save(
        IReadOnlyList<VectorPoint<TKey>> points,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        var prepared = new PreparedPoint[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            prepared[index] = Prepare(points[index]);
        }
        if (prepared.Length == 0)
            return new BatchResult<TKey>([], BatchAtomicity.Atomic);

        var table = Table();
        await EnsureShape(table, ct).ConfigureAwait(false);
        await using var connection = await Open(create: false, ct).ConfigureAwait(false)
            ?? throw MissingSource();
        using var transaction = connection.BeginTransaction();
        var outcomes = new BatchItemResult<TKey>[prepared.Length];
        for (var index = 0; index < prepared.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var outcome = await Upsert(connection, transaction, table, prepared[index], Scope(scope), ct).ConfigureAwait(false);
            outcomes[index] = new BatchItemResult<TKey>(index, prepared[index].Point.Id, outcome);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new BatchResult<TKey>(outcomes, BatchAtomicity.Atomic);
    }

    public async Task<VectorPoint<TKey>?> Get(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        var table = Table();
        await using var connection = await Open(create: false, ct).ConfigureAwait(false);
        if (connection is null || !await ShapeExists(connection, table, ct).ConfigureAwait(false)) return null;
        return await Read(connection, table, id, Scope(scope), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VectorPoint<TKey>?>> Get(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var result = new VectorPoint<TKey>?[ids.Count];
        if (ids.Count == 0) return result;
        var table = Table();
        await using var connection = await Open(create: false, ct).ConfigureAwait(false);
        if (connection is null || !await ShapeExists(connection, table, ct).ConfigureAwait(false)) return result;
        var scopeId = Scope(scope);
        for (var index = 0; index < ids.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            result[index] = await Read(connection, table, ids[index], scopeId, ct).ConfigureAwait(false);
        }
        return result;
    }

    public async Task<bool> Delete(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        var table = Table();
        await using var connection = await Open(create: false, ct).ConfigureAwait(false);
        if (connection is null || !await ShapeExists(connection, table, ct).ConfigureAwait(false)) return false;
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {Quote(table)} WHERE id = $id AND scope = $scope";
        command.Parameters.AddWithValue("$id", Key(id));
        command.Parameters.AddWithValue("$scope", Scope(scope));
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    public async Task<BatchResult<TKey>> Delete(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return new BatchResult<TKey>([], BatchAtomicity.Atomic);
        var table = Table();
        await using var connection = await Open(create: false, ct).ConfigureAwait(false);
        if (connection is null || !await ShapeExists(connection, table, ct).ConfigureAwait(false))
            return new BatchResult<TKey>(ids.Select((id, index) =>
                new BatchItemResult<TKey>(index, id, MutationOutcome.Missing)), BatchAtomicity.Atomic);

        using var transaction = connection.BeginTransaction();
        var outcomes = new BatchItemResult<TKey>[ids.Count];
        for (var index = 0; index < ids.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {Quote(table)} WHERE id = $id AND scope = $scope";
            command.Parameters.AddWithValue("$id", Key(ids[index]));
            command.Parameters.AddWithValue("$scope", Scope(scope));
            var removed = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            outcomes[index] = new BatchItemResult<TKey>(
                index,
                ids[index],
                removed > 0 ? MutationOutcome.Deleted : MutationOutcome.Missing);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new BatchResult<TKey>(outcomes, BatchAtomicity.Atomic);
    }

    public async Task<VectorSearchResult<TKey>> Search(
        VectorSearchRequest request,
        VectorScope scope,
        CancellationToken ct = default)
    {
        Validate(request);
        var table = Table();
        await using var connection = await Open(create: false, ct).ConfigureAwait(false);
        if (connection is null || !await ShapeExists(connection, table, ct).ConfigureAwait(false))
            return EmptyResult();

        var requested = Math.Min(_options.MaxSearchCandidates, checked(request.Top + 1));
        List<Ranked> ranked;
        while (true)
        {
            ranked = await Search(connection, table, request, Scope(scope), requested, ct).ConfigureAwait(false);
            ranked.Sort(static (left, right) =>
            {
                var distance = left.Distance.CompareTo(right.Distance);
                return distance != 0 ? distance : StringComparer.Ordinal.Compare(left.StableId, right.StableId);
            });
            if (ranked.Count <= request.Top || ranked.Count < requested ||
                ranked[request.Top - 1].Distance != ranked[request.Top].Distance)
                break;
            if (requested >= _options.MaxSearchCandidates)
                throw new InvalidOperationException(
                    $"SqliteVec cannot resolve a stable identity tie within the configured bound of {_options.MaxSearchCandidates} candidates. " +
                    "Increase MaxSearchCandidates or narrow the vector space.");
            requested = Math.Min(_options.MaxSearchCandidates, checked(requested * 2));
        }

        var output = new List<VectorMatch<TKey>>(Math.Min(request.Top, ranked.Count));
        for (var index = 0; index < ranked.Count && output.Count < request.Top; index++)
        {
            var item = ranked[index];
            var similarity = Similarity(item.Distance);
            if (request.MinimumSimilarity is not null && similarity < request.MinimumSimilarity.Value) continue;
            output.Add(new VectorMatch<TKey>(item.Id, similarity, item.Metadata));
        }
        return new VectorSearchResult<TKey>(
            output.AsReadOnly(),
            null,
            new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Exact, null));
    }

    public async Task Clear(VectorScope scope, CancellationToken ct = default)
    {
        var table = Table();
        await using var connection = await Open(create: false, ct).ConfigureAwait(false);
        if (connection is null || !await ShapeExists(connection, table, ct).ConfigureAwait(false)) return;
        await using var command = connection.CreateCommand();
        if (string.IsNullOrEmpty(scope.Identity))
        {
            command.CommandText = $"DELETE FROM {Quote(table)}";
        }
        else
        {
            command.CommandText = $"DELETE FROM {Quote(table)} WHERE scope = $scope";
            command.Parameters.AddWithValue("$scope", scope.Identity);
        }
        _ = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public Task Sync(VectorScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task VectorEnsureCreated(CancellationToken ct = default) => EnsureShape(Table(), ct);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_memoryKeeper is { IsValueCreated: true } && _memoryKeeper.Value.IsCompletedSuccessfully)
            _memoryKeeper.Value.Result.Dispose();
        ClearPools();
        _shapeGate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_memoryKeeper is { IsValueCreated: true })
            await (await _memoryKeeper.Value.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false);
        ClearPools();
        _shapeGate.Dispose();
    }

    private async Task EnsureShape(string table, CancellationToken ct)
    {
        if (_readyShapes.ContainsKey(table)) return;
        await _shapeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_readyShapes.ContainsKey(table)) return;
            await using var connection = await Open(create: true, ct).ConfigureAwait(false)
                ?? throw MissingSource();
            var sql = await Schema(connection, table, ct).ConfigureAwait(false);
            if (sql is null)
            {
                _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin, $"create SqliteVec space '{_plan.Name}'");
                await using var command = connection.CreateCommand();
                command.CommandText = CreateSql(table);
                _ = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                sql = await Schema(connection, table, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"SqliteVec did not create space '{_plan.Name}'.");
            }
            ValidateSchema(sql);
            _readyShapes.TryAdd(table, 0);
        }
        finally
        {
            _shapeGate.Release();
        }
    }

    private async Task<bool> ShapeExists(SqliteConnection connection, string table, CancellationToken ct)
    {
        if (_readyShapes.ContainsKey(table)) return true;
        var sql = await Schema(connection, table, ct).ConfigureAwait(false);
        if (sql is null) return false;
        ValidateSchema(sql);
        _readyShapes.TryAdd(table, 0);
        return true;
    }

    private async Task<SqliteConnection?> Open(bool create, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ct.ThrowIfCancellationRequested();
        var parsed = Parse(_connectionString);
        if (IsMemory(parsed))
        {
            _ = await _memoryKeeper!.Value.WaitAsync(ct).ConfigureAwait(false);
        }
        else
        {
            var path = System.IO.Path.GetFullPath(parsed.DataSource);
            var exists = File.Exists(path);
            if (!exists && !create) return null;
            if (!exists)
            {
                _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin, $"create SqliteVec source for '{_plan.Name}'");
                var directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            }
            if (_route.Policy.Access == DataSourceAccess.ReadOnly)
                parsed.Mode = SqliteOpenMode.ReadOnly;
            else if (parsed.Mode != SqliteOpenMode.ReadOnly)
                parsed.Mode = exists ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadWriteCreate;
        }

        var effectiveConnection = parsed.ToString();
        _poolGroups.TryAdd(effectiveConnection, 0);
        var connection = new SqliteConnection(effectiveConnection);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            _native.Load(connection);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<SqliteConnection> OpenMemoryKeeper()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    private void ClearPools()
    {
        foreach (var connectionString in _poolGroups.Keys)
            SqliteConnection.ClearPool(new SqliteConnection(connectionString));
        _poolGroups.Clear();
    }

    private async Task<MutationOutcome> Upsert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        PreparedPoint prepared,
        string scope,
        CancellationToken ct)
    {
        var existed = await Exists(connection, transaction, table, prepared.Key, scope, ct).ConfigureAwait(false);
        await using (var remove = connection.CreateCommand())
        {
            remove.Transaction = transaction;
            remove.CommandText = $"DELETE FROM {Quote(table)} WHERE id = $id AND scope = $scope";
            remove.Parameters.AddWithValue("$id", prepared.Key);
            remove.Parameters.AddWithValue("$scope", scope);
            _ = await remove.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {Quote(table)}(id, embedding, scope, metadata) VALUES ($id, $embedding, $scope, $metadata)";
            insert.Parameters.AddWithValue("$id", prepared.Key);
            insert.Parameters.AddWithValue("$embedding", prepared.Embedding);
            insert.Parameters.AddWithValue("$scope", scope);
            insert.Parameters.AddWithValue("$metadata", (object?)prepared.Metadata ?? DBNull.Value);
            _ = await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return existed ? MutationOutcome.Updated : MutationOutcome.Inserted;
    }

    private async Task<bool> Exists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string key,
        string scope,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT 1 FROM {Quote(table)} WHERE id = $id AND scope = $scope LIMIT 1";
        command.Parameters.AddWithValue("$id", key);
        command.Parameters.AddWithValue("$scope", scope);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private async Task<VectorPoint<TKey>?> Read(
        SqliteConnection connection,
        string table,
        TKey id,
        string scope,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT embedding, metadata FROM {Quote(table)} WHERE id = $id AND scope = $scope LIMIT 1";
        command.Parameters.AddWithValue("$id", Key(id));
        command.Parameters.AddWithValue("$scope", scope);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new VectorPoint<TKey>(
            id,
            FromBlob(reader.GetFieldValue<byte[]>(0)),
            reader.IsDBNull(1) ? null : VectorMetadata.FromJson(reader.GetString(1)));
    }

    private async Task<List<Ranked>> Search(
        SqliteConnection connection,
        string table,
        VectorSearchRequest request,
        string scope,
        int candidates,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT id, distance, metadata FROM {Quote(table)} " +
            "WHERE embedding MATCH $embedding AND k = $candidates AND scope = $scope " +
            "ORDER BY distance";
        command.Parameters.AddWithValue("$embedding", ToBlob(request.Embedding.Span));
        command.Parameters.AddWithValue("$candidates", candidates);
        command.Parameters.AddWithValue("$scope", scope);
        var result = new List<Ranked>(candidates);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var stable = reader.GetString(0);
            var distance = reader.GetDouble(1);
            if (!double.IsFinite(distance) || distance < 0)
                throw new InvalidOperationException("SqliteVec returned a non-finite or negative distance.");
            result.Add(new Ranked(
                ParseKey(stable),
                stable,
                distance,
                reader.IsDBNull(2) ? null : VectorMetadata.FromJson(reader.GetString(2))));
        }
        return result;
    }

    private PreparedPoint Prepare(VectorPoint<TKey> point)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateEmbedding(point.Embedding.Span, nameof(point));
        var metadata = VectorMetadata.ToJson(point.Metadata);
        if (metadata is not null && Encoding.UTF8.GetByteCount(metadata) > _options.MaxMetadataBytesPerPoint)
            throw new InvalidOperationException(
                $"Vector metadata exceeds SqliteVec's configured bound of {_options.MaxMetadataBytesPerPoint} UTF-8 bytes per point. " +
                "Reduce metadata or increase MaxMetadataBytesPerPoint.");
        return new PreparedPoint(point, Key(point.Id), ToBlob(point.Embedding.Span), metadata);
    }

    private void Validate(VectorSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEmbedding(request.Embedding.Span, nameof(request));
        if (request.Top <= 0) throw new ArgumentOutOfRangeException(nameof(request.Top));
        if (request.Top >= _options.MaxSearchCandidates)
            throw new InvalidOperationException(
                $"SqliteVec Top must be smaller than the configured candidate bound of {_options.MaxSearchCandidates} " +
                "so the adapter can prove a complete stable cutoff tie. Reduce Top or increase MaxSearchCandidates.");
        if (!string.Equals(request.Space, _plan.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Vector request targets space '{request.Space}', but SqliteVec is bound to '{_plan.Name}'.");
        if (request.Filter is not null)
            throw new NotSupportedException(
                "SqliteVec does not claim arbitrary metadata filtering because its neutral metadata is one auxiliary JSON value. Remove Where(...) or select an adapter with native filter-before-rank support.");
        if (request.Text is not null || request.SemanticWeight is not null)
            throw new NotSupportedException("SqliteVec does not simulate lexical or hybrid search.");
        if (request.Continuation is not null)
            throw new NotSupportedException("SqliteVec does not claim a snapshot continuation contract.");
    }

    private void ValidateEmbedding(ReadOnlySpan<float> embedding, string parameter)
    {
        if (embedding.Length != _plan.Dimensions)
            throw new ArgumentException(
                $"Vector embedding has {embedding.Length} dimensions; space '{_plan.Name}' requires {_plan.Dimensions}.",
                parameter);
        for (var index = 0; index < embedding.Length; index++)
            if (!float.IsFinite(embedding[index]))
                throw new ArgumentException($"Vector embedding contains a non-finite value at index {index}.", parameter);
    }

    private async Task<string?> Schema(SqliteConnection connection, string table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    private void ValidateSchema(string sql)
    {
        var compact = Compact(sql);
        var required = new[]
        {
            "usingvec0(",
            "idtextprimarykey",
            $"embeddingfloat[{_plan.Dimensions}]distance_metric={MetricSql.ToLowerInvariant()}",
            "scopetextpartitionkey",
            "+metadatatext"
        };
        if (required.Any(value => !compact.Contains(value, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Existing SqliteVec space '{_plan.Name}' has an incompatible shape. " +
                $"Expected {_plan.Dimensions} dimensions, {_plan.Metric}, scoped identity, and neutral metadata. " +
                "Provision the correct External shape or use a new Managed source.");
    }

    private VectorSearchResult<TKey> EmptyResult() => new(
        Array.Empty<VectorMatch<TKey>>(),
        null,
        new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Exact, 0));

    private double Similarity(double distance) => _plan.Metric switch
    {
        VectorMetric.Cosine => Math.Clamp(1d - (distance / 2d), 0d, 1d),
        VectorMetric.Euclidean => 1d / (1d + distance),
        _ => throw new InvalidOperationException($"Unsupported SqliteVec metric '{_plan.Metric}'.")
    };

    private string CreateSql(string table) =>
        $"CREATE VIRTUAL TABLE {Quote(table)} USING vec0(" +
        $"id TEXT PRIMARY KEY, embedding float[{_plan.Dimensions}] distance_metric={MetricSql}, " +
        "scope TEXT PARTITION KEY, +metadata TEXT)";

    private string MetricSql => _plan.Metric switch
    {
        VectorMetric.Cosine => "cosine",
        VectorMetric.Euclidean => "L2",
        _ => throw new InvalidOperationException($"Unsupported SqliteVec metric '{_plan.Metric}'.")
    };

    private string Table()
    {
        var logical = VectorAdapterNaming.GetOrCompute<TEntity>(_services, _factory, _plan.Source) + "\n" + _plan.Name;
        return PhysicalName(logical);
    }

    private static string Quote(string table) => $"\"{table}\"";

    private static string Scope(VectorScope scope) => scope?.Identity ?? string.Empty;

    private static SqliteConnectionStringBuilder Parse(string connectionString)
    {
        try
        {
            return new SqliteConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException error)
        {
            throw new InvalidOperationException("SqliteVec received an invalid SQLite connection string.", error);
        }
    }

    private static bool IsMemory(SqliteConnectionStringBuilder value) =>
        value.Mode == SqliteOpenMode.Memory ||
        string.Equals(value.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);

    private static string PhysicalName(string logical)
    {
        var readable = new string(logical
            .Take(36)
            .Select(static character => char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_')
            .ToArray()).Trim('_');
        if (readable.Length == 0) readable = "space";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(logical))).ToLowerInvariant()[..16];
        return $"koan_vec_{readable}_{hash}";
    }

    private static string Compact(string value) => new(
        value.Where(static character => !char.IsWhiteSpace(character) && character != '"')
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static byte[] ToBlob(ReadOnlySpan<float> embedding) =>
        MemoryMarshal.AsBytes(embedding).ToArray();

    private ReadOnlyMemory<float> FromBlob(byte[] bytes)
    {
        if (bytes.Length != _plan.Dimensions * sizeof(float))
            throw new InvalidOperationException(
                $"SqliteVec returned {bytes.Length} embedding bytes; space '{_plan.Name}' requires {_plan.Dimensions * sizeof(float)}.");
        var values = new float[_plan.Dimensions];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    private static string Key(TKey id) => id switch
    {
        string value => value,
        Guid value => value.ToString("D"),
        IFormattable value => value.ToString(null, CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException($"Vector identity type '{typeof(TKey).FullName}' produced no stable value."),
        _ => id.ToString()
            ?? throw new InvalidOperationException($"Vector identity type '{typeof(TKey).FullName}' produced no stable value.")
    };

    private static TKey ParseKey(string value)
    {
        if (typeof(TKey) == typeof(string)) return (TKey)(object)value;
        if (typeof(TKey) == typeof(Guid)) return (TKey)(object)Guid.ParseExact(value, "D");
        var converter = TypeDescriptor.GetConverter(typeof(TKey));
        if (converter.CanConvertFrom(typeof(string)))
            return (TKey)(converter.ConvertFromInvariantString(value)
                ?? throw new InvalidOperationException($"Vector identity '{value}' could not be converted to '{typeof(TKey).FullName}'."));
        return (TKey)Convert.ChangeType(value, typeof(TKey), CultureInfo.InvariantCulture);
    }

    private InvalidOperationException MissingSource() => new(
        $"SqliteVec source '{_route.Source}' does not exist. Save through a Managed source or provision the External file and vector space first.");

    private sealed record PreparedPoint(VectorPoint<TKey> Point, string Key, byte[] Embedding, string? Metadata);
    private sealed record Ranked(TKey Id, string StableId, double Distance, DataObject? Metadata);
}
