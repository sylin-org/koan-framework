using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Naming;
using Npgsql;
using NpgsqlTypes;

namespace Koan.Data.Vector.Connector.PgVector;

internal sealed class PgVectorRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IDisposable,
    IAsyncDisposable
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;
    private readonly PgVectorVectorAdapterFactory _factory;
    private readonly VectorSpacePlan _plan;
    private readonly PgVectorRoute _route;
    private readonly PgVectorOptions _options;
    private readonly ConcurrentDictionary<string, byte> _readyShapes = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _shapeGate = new(1, 1);
    private int _disposed;

    internal PgVectorRepository(
        IServiceProvider services,
        PgVectorVectorAdapterFactory factory,
        VectorSpacePlan plan,
        PgVectorRoute route,
        PgVectorOptions options)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Describe(ICapabilities capabilities) => capabilities
        .Add(VectorCaps.Knn)
        .Add(VectorCaps.Filters, PgVectorFilter.Capabilities)
        .Add(VectorCaps.BulkUpsert)
        .Add(VectorCaps.BulkDelete)
        .Add(VectorCaps.ScoreNormalization)
        .Add(VectorCaps.DynamicCollections);

    public async Task Save(VectorPoint<TKey> point, VectorScope scope, CancellationToken ct = default)
    {
        var prepared = Prepare(point);
        _route.Policy.Demand(DataOperationEffect.Write, $"save pgvector point in space '{_plan.Name}'");
        var table = Table();
        await EnsureShape(table, ct).ConfigureAwait(false);
        await using var connection = await Open(ct).ConfigureAwait(false);
        _ = await Upsert(connection, table, prepared, Scope(scope), ct).ConfigureAwait(false);
    }

    public async Task<BatchResult<TKey>> Save(
        IReadOnlyList<VectorPoint<TKey>> points,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        DemandBatch(points.Count);
        var prepared = new PreparedPoint[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            prepared[index] = Prepare(points[index]);
        }
        if (prepared.Length == 0)
            return new BatchResult<TKey>([], BatchAtomicity.NotGuaranteed);
        DemandUnique(prepared.Select(static point => point.Key), "save");

        _route.Policy.Demand(DataOperationEffect.Write, $"save pgvector batch in space '{_plan.Name}'");
        var table = Table();
        await EnsureShape(table, ct).ConfigureAwait(false);
        await using var connection = await Open(ct).ConfigureAwait(false);
        var outcomes = await UpsertBatch(connection, table, prepared, Scope(scope), ct).ConfigureAwait(false);
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    public async Task<VectorPoint<TKey>?> Get(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        var table = Table();
        await using var connection = await Open(ct).ConfigureAwait(false);
        if (!await ShapeExists(connection, table, ct).ConfigureAwait(false)) return null;
        return await Read(connection, table, id, scope, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VectorPoint<TKey>?>> Get(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        DemandBatch(ids.Count);
        var output = new VectorPoint<TKey>?[ids.Count];
        if (ids.Count == 0) return output;
        var table = Table();
        await using var connection = await Open(ct).ConfigureAwait(false);
        if (!await ShapeExists(connection, table, ct).ConfigureAwait(false)) return output;
        for (var index = 0; index < ids.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            output[index] = await Read(connection, table, ids[index], scope, ct).ConfigureAwait(false);
        }
        return output;
    }

    public async Task<bool> Delete(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        var key = Key(id);
        _route.Policy.Demand(DataOperationEffect.Write, $"delete pgvector point from space '{_plan.Name}'");
        var table = Table();
        await using var connection = await Open(ct).ConfigureAwait(false);
        if (!await ShapeExists(connection, table, ct).ConfigureAwait(false)) return false;
        return await DeleteOne(connection, table, key, scope, ct).ConfigureAwait(false);
    }

    public async Task<BatchResult<TKey>> Delete(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        DemandBatch(ids.Count);
        if (ids.Count == 0)
            return new BatchResult<TKey>([], BatchAtomicity.NotGuaranteed);
        var keys = ids.Select(Key).ToArray();
        DemandUnique(keys, "delete");
        _route.Policy.Demand(DataOperationEffect.Write, $"delete pgvector batch from space '{_plan.Name}'");
        var table = Table();
        await using var connection = await Open(ct).ConfigureAwait(false);
        if (!await ShapeExists(connection, table, ct).ConfigureAwait(false))
            return Missing(ids);

        var outcomes = await DeleteBatch(connection, table, ids, keys, scope, ct).ConfigureAwait(false);
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    public async Task<VectorSearchResult<TKey>> Search(
        VectorSearchRequest request,
        VectorScope scope,
        CancellationToken ct = default)
    {
        Validate(request);
        var table = Table();
        await using var connection = await Open(ct).ConfigureAwait(false);
        if (!await ShapeExists(connection, table, ct).ConfigureAwait(false)) return EmptyResult();

        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var exact = Command(
                         connection,
                         "SET LOCAL enable_seqscan = on; SET LOCAL enable_indexscan = off; " +
                         "SET LOCAL enable_indexonlyscan = off; SET LOCAL enable_bitmapscan = off",
                         transaction))
        {
            _ = await exact.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var requested = Math.Min(request.Top, _options.MaxSearchCandidates);
        var output = new List<VectorMatch<TKey>>(requested);
        await using (var command = Command(connection, string.Empty, transaction))
        {
            command.Parameters.AddWithValue("query", ToVectorLiteral(request.Embedding.Span));
            command.Parameters.AddWithValue("scope", Scope(scope));
            command.Parameters.AddWithValue("limit", requested);
            var effectiveFilter = request.Filter is null
                ? scope.Predicate
                : scope.Predicate is null
                    ? request.Filter
                    : Filter.All(request.Filter, scope.Predicate);
            var filter = PgVectorFilter.Compile(
                effectiveFilter,
                command,
                Quote(Infrastructure.Constants.Schema.FilterData));
            var distance = $"{Quote(Infrastructure.Constants.Schema.Embedding)} {DistanceOperator()} CAST(@query AS vector)";
            command.CommandText =
                $"SELECT {Quote(Infrastructure.Constants.Schema.Id)}, " +
                $"{Quote(Infrastructure.Constants.Schema.Metadata)}::text, {distance} AS distance " +
                $"FROM {Quote(table)} " +
                $"WHERE {Quote(Infrastructure.Constants.Schema.Scope)} = @scope " +
                $"AND ({filter}) " +
                $"ORDER BY distance, {Quote(Infrastructure.Constants.Schema.Id)} COLLATE \"C\" LIMIT @limit";

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var rawDistance = reader.GetDouble(2);
                if (!double.IsFinite(rawDistance))
                    throw new InvalidOperationException("PgVector returned a non-finite vector distance.");
                var similarity = Similarity(rawDistance);
                if (request.MinimumSimilarity is not null && similarity < request.MinimumSimilarity.Value) continue;
                output.Add(new VectorMatch<TKey>(
                    ParseKey(reader.GetString(0)),
                    similarity,
                    reader.IsDBNull(1) ? null : VectorMetadata.FromJson(reader.GetString(1))));
            }
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new VectorSearchResult<TKey>(
            output.AsReadOnly(),
            null,
            new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Exact, null));
    }

    public async Task Clear(VectorScope scope, CancellationToken ct = default)
    {
        _route.Policy.Demand(DataOperationEffect.Write, $"clear pgvector space '{_plan.Name}'");
        var table = Table();
        await using var connection = await Open(ct).ConfigureAwait(false);
        if (!await ShapeExists(connection, table, ct).ConfigureAwait(false)) return;
        await using var command = Command(connection, string.Empty);
        command.Parameters.AddWithValue("scope", Scope(scope));
        var predicate = PgVectorFilter.Compile(scope.Predicate, command, Quote(Infrastructure.Constants.Schema.FilterData));
        command.CommandText =
            $"DELETE FROM {Quote(table)} WHERE {Quote(Infrastructure.Constants.Schema.Scope)} = @scope AND ({predicate})";
        _ = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public Task Sync(VectorScope scope, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task VectorEnsureCreated(CancellationToken ct = default)
    {
        _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin, $"create pgvector space '{_plan.Name}'");
        return EnsureShape(Table(), ct);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shapeGate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task EnsureShape(string table, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_readyShapes.ContainsKey(table)) return;
        await _shapeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_readyShapes.ContainsKey(table)) return;
            await using var connection = await OpenOrCreate(ct).ConfigureAwait(false);
            var shape = await InspectShape(connection, null, table, ct).ConfigureAwait(false);
            if (shape is null)
            {
                _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin, $"create pgvector space '{_plan.Name}'");
                await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
                // Extension creation is database-wide; every shape creator takes this lock first so two Entity
                // tables cannot race PostgreSQL's non-atomic CREATE EXTENSION IF NOT EXISTS path.
                await AdvisoryLock(connection, transaction, "extension", ct).ConfigureAwait(false);
                await AdvisoryLock(connection, transaction, $"table:{table}", ct).ConfigureAwait(false);
                shape = await InspectShape(connection, transaction, table, ct).ConfigureAwait(false);
                if (shape is null)
                {
                    await EnsureExtension(connection, transaction, ct).ConfigureAwait(false);
                    await CreateShape(connection, transaction, table, ct).ConfigureAwait(false);
                    shape = await InspectShape(connection, transaction, table, ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"PgVector did not create native space '{table}'.");
                }
                ValidateShape(table, shape);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
            else
            {
                ValidateShape(table, shape);
            }
            _readyShapes.TryAdd(table, 0);
        }
        finally
        {
            _shapeGate.Release();
        }
    }

    private async Task<bool> ShapeExists(NpgsqlConnection connection, string table, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_readyShapes.ContainsKey(table)) return true;
        var shape = await InspectShape(connection, null, table, ct).ConfigureAwait(false);
        if (shape is null) return false;
        ValidateShape(table, shape);
        _readyShapes.TryAdd(table, 0);
        return true;
    }

    private async Task<NativeShape?> InspectShape(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string table,
        CancellationToken ct)
    {
        await using var command = Command(connection, """
            SELECT
              (SELECT format_type(column_info.atttypid, column_info.atttypmod)
                 FROM pg_attribute AS column_info
                WHERE column_info.attrelid = relation.oid
                  AND column_info.attname = @embedding
                  AND NOT column_info.attisdropped),
              obj_description(relation.oid, 'pg_class'),
              (SELECT format_type(column_info.atttypid, column_info.atttypmod)
                 FROM pg_attribute AS column_info
                WHERE column_info.attrelid = relation.oid
                  AND column_info.attname = @id
                  AND NOT column_info.attisdropped),
              (SELECT format_type(column_info.atttypid, column_info.atttypmod)
                 FROM pg_attribute AS column_info
                WHERE column_info.attrelid = relation.oid
                  AND column_info.attname = @scope
                  AND NOT column_info.attisdropped),
              (SELECT format_type(column_info.atttypid, column_info.atttypmod)
                 FROM pg_attribute AS column_info
                WHERE column_info.attrelid = relation.oid
                  AND column_info.attname = @metadata
                  AND NOT column_info.attisdropped),
              (SELECT format_type(column_info.atttypid, column_info.atttypmod)
                 FROM pg_attribute AS column_info
                WHERE column_info.attrelid = relation.oid
                  AND column_info.attname = @filter_data
                  AND NOT column_info.attisdropped),
              (SELECT string_agg(key_column.attname::text, ',' ORDER BY key_part.ordinality)
                 FROM pg_constraint AS key_constraint
                 CROSS JOIN LATERAL unnest(key_constraint.conkey)
                    WITH ORDINALITY AS key_part(attnum, ordinality)
                 JOIN pg_attribute AS key_column
                   ON key_column.attrelid = key_constraint.conrelid
                  AND key_column.attnum = key_part.attnum
                WHERE key_constraint.conrelid = relation.oid
                  AND key_constraint.contype = 'p'),
              (SELECT column_info.attnotnull
                 FROM pg_attribute AS column_info
                WHERE column_info.attrelid = relation.oid
                  AND column_info.attname = @embedding
                  AND NOT column_info.attisdropped),
              (SELECT column_info.attnotnull
                 FROM pg_attribute AS column_info
                WHERE column_info.attrelid = relation.oid
                  AND column_info.attname = @id
                  AND NOT column_info.attisdropped),
              (SELECT column_info.attnotnull
                 FROM pg_attribute AS column_info
                WHERE column_info.attrelid = relation.oid
                  AND column_info.attname = @scope
                  AND NOT column_info.attisdropped),
              (SELECT column_info.attnotnull
                 FROM pg_attribute AS column_info
                WHERE column_info.attrelid = relation.oid
                  AND column_info.attname = @metadata
                  AND NOT column_info.attisdropped),
              (SELECT column_info.attnotnull
                 FROM pg_attribute AS column_info
                WHERE column_info.attrelid = relation.oid
                  AND column_info.attname = @filter_data
                  AND NOT column_info.attisdropped),
              (SELECT key_constraint.condeferrable
                 FROM pg_constraint AS key_constraint
                WHERE key_constraint.conrelid = relation.oid
                  AND key_constraint.contype = 'p'),
              (SELECT string_agg(column_info.attname::text, ',' ORDER BY column_info.attnum)
                 FROM pg_attribute AS column_info
                WHERE column_info.attrelid = relation.oid
                  AND column_info.attnum > 0
                  AND NOT column_info.attisdropped
                  AND column_info.attnotnull
                  AND NOT column_info.atthasdef
                  AND column_info.attgenerated = ''
                  AND column_info.attidentity = ''
                  AND column_info.attname::text NOT IN (@embedding, @id, @scope, @metadata, @filter_data))
            FROM pg_class AS relation
            JOIN pg_namespace AS namespace ON namespace.oid = relation.relnamespace
            WHERE namespace.nspname = current_schema()
              AND relation.relname = @table
              AND relation.relkind IN ('r', 'p')
            """, transaction);
        command.Parameters.AddWithValue("embedding", Infrastructure.Constants.Schema.Embedding);
        command.Parameters.AddWithValue("id", Infrastructure.Constants.Schema.Id);
        command.Parameters.AddWithValue("scope", Infrastructure.Constants.Schema.Scope);
        command.Parameters.AddWithValue("metadata", Infrastructure.Constants.Schema.Metadata);
        command.Parameters.AddWithValue("filter_data", Infrastructure.Constants.Schema.FilterData);
        command.Parameters.AddWithValue("table", table);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new NativeShape(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetBoolean(7),
            reader.IsDBNull(8) ? null : reader.GetBoolean(8),
            reader.IsDBNull(9) ? null : reader.GetBoolean(9),
            reader.IsDBNull(10) ? null : reader.GetBoolean(10),
            reader.IsDBNull(11) ? null : reader.GetBoolean(11),
            reader.IsDBNull(12) ? null : reader.GetBoolean(12),
            reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    private async Task AdvisoryLock(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        CancellationToken ct)
    {
        await using var command = Command(
            connection,
            "SELECT pg_advisory_xact_lock(hashtextextended(@name, 0))",
            transaction);
        command.Parameters.AddWithValue("name", $"koan:pgvector:{table}");
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureExtension(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken ct)
    {
        try
        {
            await using var command = Command(
                connection,
                $"CREATE EXTENSION IF NOT EXISTS {Infrastructure.Constants.Schema.Extension}",
                transaction);
            _ = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException error) when (error.SqlState == "42501")
        {
            throw new InvalidOperationException(
                "PgVector cannot enable the PostgreSQL vector extension with the configured role. Ask an administrator to run CREATE EXTENSION IF NOT EXISTS vector.",
                error);
        }
        catch (PostgresException error)
        {
            throw new InvalidOperationException(
                "The selected PostgreSQL server cannot enable the vector extension. Install pgvector on that server or select another vector adapter.",
                error);
        }
    }

    private async Task CreateShape(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        CancellationToken ct)
    {
        await using (var create = Command(connection,
            $"CREATE TABLE {Quote(table)} (" +
            $"{Quote(Infrastructure.Constants.Schema.Id)} text NOT NULL, " +
            $"{Quote(Infrastructure.Constants.Schema.Scope)} text NOT NULL, " +
            $"{Quote(Infrastructure.Constants.Schema.Embedding)} vector({_plan.Dimensions}) NOT NULL, " +
            $"{Quote(Infrastructure.Constants.Schema.Metadata)} json NULL, " +
            $"{Quote(Infrastructure.Constants.Schema.FilterData)} jsonb NULL, " +
            $"PRIMARY KEY ({Quote(Infrastructure.Constants.Schema.Scope)}, {Quote(Infrastructure.Constants.Schema.Id)}))",
            transaction))
        {
            _ = await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var marker = ShapeMarker();
        await using var comment = Command(
            connection,
            $"COMMENT ON TABLE {Quote(table)} IS '{marker}'",
            transaction);
        _ = await comment.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private void ValidateShape(string table, NativeShape shape)
    {
        var type = shape.EmbeddingType;
        if (type is null || !type.StartsWith("vector(", StringComparison.Ordinal) || !type.EndsWith(')') ||
            !int.TryParse(type.AsSpan(7, type.Length - 8), NumberStyles.None, CultureInfo.InvariantCulture, out var dimension))
            throw WrongShape(table, "the embedding column is not a dimensioned pgvector vector");
        if (dimension != _plan.Dimensions)
            throw WrongShape(table, $"dimension is {dimension}, expected {_plan.Dimensions}");
        RequireColumn(table, Infrastructure.Constants.Schema.Id, shape.IdType, "text");
        RequireColumn(table, Infrastructure.Constants.Schema.Scope, shape.ScopeType, "text");
        RequireColumn(table, Infrastructure.Constants.Schema.Metadata, shape.MetadataType, "json");
        RequireColumn(table, Infrastructure.Constants.Schema.FilterData, shape.FilterDataType, "jsonb");
        RequireNullability(table, Infrastructure.Constants.Schema.Embedding, shape.EmbeddingNotNull, expected: true);
        RequireNullability(table, Infrastructure.Constants.Schema.Id, shape.IdNotNull, expected: true);
        RequireNullability(table, Infrastructure.Constants.Schema.Scope, shape.ScopeNotNull, expected: true);
        RequireNullability(table, Infrastructure.Constants.Schema.Metadata, shape.MetadataNotNull, expected: false);
        RequireNullability(table, Infrastructure.Constants.Schema.FilterData, shape.FilterDataNotNull, expected: false);
        if (!string.Equals(shape.PrimaryKey, "scope,id", StringComparison.Ordinal))
            throw WrongShape(table, "the primary key must be (scope, id)");
        if (shape.PrimaryKeyDeferrable is not false)
            throw WrongShape(table, "the primary key must be non-deferrable for native upsert");
        if (!string.IsNullOrWhiteSpace(shape.ExtraRequiredColumns))
            throw WrongShape(table,
                $"extra required columns have no default: {shape.ExtraRequiredColumns}");
        if (shape.Comment is null || !TryReadMarker(shape.Comment, out var marker))
            throw WrongShape(table, "the immutable Koan vector-space marker is absent");
        if (!string.Equals(marker.Space, _plan.Name, StringComparison.Ordinal))
            throw WrongShape(table, $"space is '{marker.Space}', expected '{_plan.Name}'");
        if (!string.Equals(marker.Metric, _plan.Metric.ToString(), StringComparison.Ordinal))
            throw WrongShape(table, $"metric is '{marker.Metric}', expected '{_plan.Metric}'");
        if (!string.Equals(marker.Model, _plan.Model, StringComparison.Ordinal))
            throw WrongShape(table, $"model is '{marker.Model ?? "<none>"}', expected '{_plan.Model ?? "<none>"}'");
    }

    private void RequireColumn(string table, string column, string? actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw WrongShape(table, $"column '{column}' is '{actual ?? "missing"}', expected '{expected}'");
    }

    private void RequireNullability(string table, string column, bool? actual, bool expected)
    {
        if (actual != expected)
            throw WrongShape(table,
                $"column '{column}' must be {(expected ? "NOT NULL" : "nullable")}");
    }

    private async Task<MutationOutcome> Upsert(
        NpgsqlConnection connection,
        string table,
        PreparedPoint prepared,
        string scope,
        CancellationToken ct)
    {
        await using var command = Command(connection,
            $"WITH prior AS (SELECT 1 FROM {Quote(table)} " +
            $"WHERE {Quote(Infrastructure.Constants.Schema.Scope)} = @scope " +
            $"AND {Quote(Infrastructure.Constants.Schema.Id)} = @id), " +
            $"mutation AS (INSERT INTO {Quote(table)} (" +
            $"{Quote(Infrastructure.Constants.Schema.Id)}, {Quote(Infrastructure.Constants.Schema.Scope)}, " +
            $"{Quote(Infrastructure.Constants.Schema.Embedding)}, {Quote(Infrastructure.Constants.Schema.Metadata)}, " +
            $"{Quote(Infrastructure.Constants.Schema.FilterData)}) " +
            "VALUES (@id, @scope, CAST(@embedding AS vector), CAST(@metadata AS json), CAST(@filter AS jsonb)) " +
            $"ON CONFLICT ({Quote(Infrastructure.Constants.Schema.Scope)}, {Quote(Infrastructure.Constants.Schema.Id)}) " +
            $"DO UPDATE SET {Quote(Infrastructure.Constants.Schema.Embedding)} = EXCLUDED.{Quote(Infrastructure.Constants.Schema.Embedding)}, " +
            $"{Quote(Infrastructure.Constants.Schema.Metadata)} = EXCLUDED.{Quote(Infrastructure.Constants.Schema.Metadata)}, " +
            $"{Quote(Infrastructure.Constants.Schema.FilterData)} = EXCLUDED.{Quote(Infrastructure.Constants.Schema.FilterData)} RETURNING 1) " +
            "SELECT EXISTS(SELECT 1 FROM prior), (SELECT count(*) FROM mutation)");
        command.Parameters.AddWithValue("id", prepared.Key);
        command.Parameters.AddWithValue("scope", scope);
        command.Parameters.AddWithValue("embedding", prepared.Embedding);
        command.Parameters.Add("metadata", NpgsqlDbType.Text).Value =
            (object?)prepared.Metadata ?? DBNull.Value;
        command.Parameters.Add("filter", NpgsqlDbType.Text).Value =
            (object?)prepared.FilterData ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false) || reader.GetInt64(1) != 1)
            throw new InvalidOperationException("PgVector did not confirm the awaited point mutation.");
        return reader.GetBoolean(0) ? MutationOutcome.Updated : MutationOutcome.Inserted;
    }

    private async Task<BatchItemResult<TKey>[]> UpsertBatch(
        NpgsqlConnection connection,
        string table,
        IReadOnlyList<PreparedPoint> prepared,
        string scope,
        CancellationToken ct)
    {
        await using var command = Command(connection, string.Empty);
        command.Parameters.AddWithValue("scope", scope);
        var values = new StringBuilder();
        for (var index = 0; index < prepared.Count; index++)
        {
            if (index > 0) values.Append(',');
            values.Append($"({index}, @id{index}, CAST(@embedding{index} AS vector), " +
                          $"CAST(@metadata{index} AS json), CAST(@filter{index} AS jsonb))");
            command.Parameters.AddWithValue($"id{index}", prepared[index].Key);
            command.Parameters.AddWithValue($"embedding{index}", prepared[index].Embedding);
            command.Parameters.Add($"metadata{index}", NpgsqlDbType.Text).Value =
                (object?)prepared[index].Metadata ?? DBNull.Value;
            command.Parameters.Add($"filter{index}", NpgsqlDbType.Text).Value =
                (object?)prepared[index].FilterData ?? DBNull.Value;
        }

        command.CommandText =
            $"WITH input(ordinal, id, embedding, metadata, filter_data) AS (VALUES {values}), " +
            $"prior AS MATERIALIZED (SELECT input.ordinal FROM input JOIN {Quote(table)} AS stored " +
            $"ON stored.{Quote(Infrastructure.Constants.Schema.Scope)} = @scope " +
            $"AND stored.{Quote(Infrastructure.Constants.Schema.Id)} = input.id), " +
            $"mutation AS (INSERT INTO {Quote(table)} (" +
            $"{Quote(Infrastructure.Constants.Schema.Id)}, {Quote(Infrastructure.Constants.Schema.Scope)}, " +
            $"{Quote(Infrastructure.Constants.Schema.Embedding)}, {Quote(Infrastructure.Constants.Schema.Metadata)}, " +
            $"{Quote(Infrastructure.Constants.Schema.FilterData)}) " +
            "SELECT input.id, @scope, input.embedding, input.metadata, input.filter_data FROM input ORDER BY input.ordinal " +
            $"ON CONFLICT ({Quote(Infrastructure.Constants.Schema.Scope)}, {Quote(Infrastructure.Constants.Schema.Id)}) " +
            $"DO UPDATE SET {Quote(Infrastructure.Constants.Schema.Embedding)} = EXCLUDED.{Quote(Infrastructure.Constants.Schema.Embedding)}, " +
            $"{Quote(Infrastructure.Constants.Schema.Metadata)} = EXCLUDED.{Quote(Infrastructure.Constants.Schema.Metadata)}, " +
            $"{Quote(Infrastructure.Constants.Schema.FilterData)} = EXCLUDED.{Quote(Infrastructure.Constants.Schema.FilterData)} " +
            $"RETURNING {Quote(Infrastructure.Constants.Schema.Id)}) " +
            "SELECT input.ordinal, prior.ordinal IS NOT NULL, (SELECT count(*) FROM mutation) " +
            "FROM input LEFT JOIN prior USING (ordinal) ORDER BY input.ordinal";

        var outcomes = new BatchItemResult<TKey>[prepared.Count];
        var rows = 0;
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var index = reader.GetInt32(0);
            if (index < 0 || index >= prepared.Count || reader.GetInt64(2) != prepared.Count)
                throw new InvalidOperationException("PgVector did not confirm the awaited batch mutation.");
            outcomes[index] = new BatchItemResult<TKey>(
                index,
                prepared[index].Point.Id,
                reader.GetBoolean(1) ? MutationOutcome.Updated : MutationOutcome.Inserted);
            rows++;
        }
        if (rows != prepared.Count)
            throw new InvalidOperationException("PgVector did not return every awaited batch mutation outcome.");
        return outcomes;
    }

    private async Task<VectorPoint<TKey>?> Read(
        NpgsqlConnection connection,
        string table,
        TKey id,
        VectorScope scope,
        CancellationToken ct)
    {
        await using var command = Command(connection, string.Empty);
        command.Parameters.AddWithValue("id", Key(id));
        command.Parameters.AddWithValue("scope", Scope(scope));
        var predicate = PgVectorFilter.Compile(scope.Predicate, command, Quote(Infrastructure.Constants.Schema.FilterData));
        command.CommandText =
            $"SELECT {Quote(Infrastructure.Constants.Schema.Embedding)}::text, " +
            $"{Quote(Infrastructure.Constants.Schema.Metadata)}::text FROM {Quote(table)} " +
            $"WHERE {Quote(Infrastructure.Constants.Schema.Scope)} = @scope " +
            $"AND {Quote(Infrastructure.Constants.Schema.Id)} = @id AND ({predicate}) LIMIT 1";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new VectorPoint<TKey>(
            id,
            ParseVector(reader.GetString(0)),
            reader.IsDBNull(1) ? null : VectorMetadata.FromJson(reader.GetString(1)));
    }

    private async Task<bool> DeleteOne(
        NpgsqlConnection connection,
        string table,
        string key,
        VectorScope scope,
        CancellationToken ct)
    {
        await using var command = Command(connection, string.Empty);
        command.Parameters.AddWithValue("id", key);
        command.Parameters.AddWithValue("scope", Scope(scope));
        var predicate = PgVectorFilter.Compile(scope.Predicate, command, Quote(Infrastructure.Constants.Schema.FilterData));
        command.CommandText =
            $"DELETE FROM {Quote(table)} WHERE {Quote(Infrastructure.Constants.Schema.Scope)} = @scope " +
            $"AND {Quote(Infrastructure.Constants.Schema.Id)} = @id AND ({predicate})";
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private async Task<BatchItemResult<TKey>[]> DeleteBatch(
        NpgsqlConnection connection,
        string table,
        IReadOnlyList<TKey> ids,
        IReadOnlyList<string> keys,
        VectorScope scope,
        CancellationToken ct)
    {
        await using var command = Command(connection, string.Empty);
        command.Parameters.AddWithValue("scope", Scope(scope));
        var values = new StringBuilder();
        for (var index = 0; index < keys.Count; index++)
        {
            if (index > 0) values.Append(',');
            values.Append($"({index}, @id{index})");
            command.Parameters.AddWithValue($"id{index}", keys[index]);
        }
        var predicate = PgVectorFilter.Compile(
            scope.Predicate,
            command,
            $"stored.{Quote(Infrastructure.Constants.Schema.FilterData)}");
        command.CommandText =
            $"WITH input(ordinal, id) AS (VALUES {values}), " +
            $"removed AS (DELETE FROM {Quote(table)} AS stored USING input " +
            $"WHERE stored.{Quote(Infrastructure.Constants.Schema.Scope)} = @scope " +
            $"AND stored.{Quote(Infrastructure.Constants.Schema.Id)} = input.id AND ({predicate}) " +
            $"RETURNING stored.{Quote(Infrastructure.Constants.Schema.Id)}) " +
            "SELECT input.ordinal, removed.id IS NOT NULL FROM input LEFT JOIN removed ON removed.id = input.id " +
            "ORDER BY input.ordinal";

        var outcomes = new BatchItemResult<TKey>[ids.Count];
        var rows = 0;
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var index = reader.GetInt32(0);
            if (index < 0 || index >= ids.Count)
                throw new InvalidOperationException("PgVector returned an invalid batch delete outcome.");
            outcomes[index] = new BatchItemResult<TKey>(
                index,
                ids[index],
                reader.GetBoolean(1) ? MutationOutcome.Deleted : MutationOutcome.Missing);
            rows++;
        }
        if (rows != ids.Count)
            throw new InvalidOperationException("PgVector did not return every awaited batch delete outcome.");
        return outcomes;
    }

    private PreparedPoint Prepare(VectorPoint<TKey> point)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateEmbedding(point.Embedding.Span, "point embedding");
        var metadata = VectorMetadata.ToJson(point.Metadata);
        var filterData = PgVectorFilter.ToIndexJson(point.Metadata);
        var metadataBytes = metadata is null ? 0 : Encoding.UTF8.GetByteCount(metadata);
        var filterBytes = filterData is null ? 0 : Encoding.UTF8.GetByteCount(filterData);
        if (metadataBytes > _options.MaxMetadataBytesPerPoint || filterBytes > _options.MaxMetadataBytesPerPoint)
            throw new InvalidOperationException(
                $"PgVector point metadata exceeds the configured {_options.MaxMetadataBytesPerPoint} byte limit.");
        return new PreparedPoint(point, Key(point.Id), ToVectorLiteral(point.Embedding.Span), metadata, filterData);
    }

    private void Validate(VectorSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEmbedding(request.Embedding.Span, "query embedding");
        if (request.Top <= 0 || request.Top > _options.MaxSearchCandidates)
            throw new ArgumentOutOfRangeException(nameof(request.Top),
                $"PgVector Top must be positive and no greater than MaxSearchCandidates ({_options.MaxSearchCandidates}).");
        if (request.Space is not null && !string.Equals(request.Space, _plan.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"PgVector query requested space '{request.Space}'. Available space: {_plan.Name}.");
        if (request.Text is not null || request.SemanticWeight is not null)
            throw new NotSupportedException("PgVector does not claim portable hybrid text semantics.");
        if (request.Continuation is not null)
            throw new NotSupportedException("PgVector does not claim a stable search continuation snapshot.");
        if (request.MinimumSimilarity is < 0d or > 1d ||
            request.MinimumSimilarity is { } minimum && !double.IsFinite(minimum))
            throw new ArgumentOutOfRangeException(nameof(request.MinimumSimilarity),
                "Minimum similarity must be finite and in [0,1].");
    }

    private void ValidateEmbedding(ReadOnlySpan<float> embedding, string label)
    {
        if (embedding.Length != _plan.Dimensions)
            throw new ArgumentException(
                $"PgVector {label} has {embedding.Length} dimensions; space '{_plan.Name}' requires {_plan.Dimensions}.");
        double squared = 0d;
        for (var index = 0; index < embedding.Length; index++)
        {
            if (!float.IsFinite(embedding[index]))
                throw new ArgumentException($"PgVector {label} contains a non-finite value at index {index}.");
            squared += embedding[index] * (double)embedding[index];
        }
        if (squared == 0d && _plan.Metric == VectorMetric.Cosine)
            throw new ArgumentException("PgVector cosine spaces do not accept a zero-magnitude embedding.");
    }

    private async Task<NpgsqlConnection> Open(CancellationToken ct)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        var connection = new NpgsqlConnection(_route.ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // The server answered and the credentials work; the Koan database does not exist yet. Managed
    // lifecycle creates it against the server's always-present postgres database before the first
    // vector write — extension and table provisioning then run inside EnsureShape.
    private async Task<NpgsqlConnection> OpenOrCreate(CancellationToken ct)
    {
        try
        {
            return await Open(ct).ConfigureAwait(false);
        }
        catch (PostgresException error) when (error.SqlState == "3D000")
        {
            var builder = new NpgsqlConnectionStringBuilder(_route.ConnectionString);
            var database = builder.Database;
            builder.Database = "postgres";
            await using var maintenance = new NpgsqlConnection(builder.ConnectionString);
            await maintenance.OpenAsync(ct).ConfigureAwait(false);
            await using var check = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = $1", maintenance)
            { Parameters = { new NpgsqlParameter { Value = database } } };
            if (await check.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            {
                try
                {
                    await using var create = new NpgsqlCommand(
                        $"CREATE DATABASE {QuoteIdentifier(database)}", maintenance);
                    await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                catch (PostgresException race) when (race.SqlState == "42P04")
                {
                    // Another provisioner created it concurrently.
                }
            }
            return await Open(ct).ConfigureAwait(false);
        }
    }

    private static string QuoteIdentifier(string identifier) =>
        '"' + identifier.Replace("\"", "\"\"") + '"';

    private NpgsqlCommand Command(
        NpgsqlConnection connection,
        string sql,
        NpgsqlTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        command.Transaction = transaction;
        return command;
    }

    private string Table()
    {
        ThrowIfDisposed();
        return VectorAdapterNaming.GetOrCompute<TEntity>(_services, _factory, _plan.Source);
    }

    private string DistanceOperator() => _plan.Metric switch
    {
        VectorMetric.Cosine => "<=>",
        VectorMetric.Euclidean => "<->",
        VectorMetric.DotProduct => "<#>",
        _ => throw new NotSupportedException($"PgVector does not support metric '{_plan.Metric}'.")
    };

    private double Similarity(double distance)
    {
        var value = _plan.Metric switch
        {
            VectorMetric.Cosine => 1d - (distance / 2d),
            VectorMetric.Euclidean => 1d / (1d + Math.Max(0d, distance)),
            VectorMetric.DotProduct when -distance >= 0d => 1d / (1d + Math.Exp(distance)),
            VectorMetric.DotProduct => Math.Exp(-distance) / (1d + Math.Exp(-distance)),
            _ => throw new NotSupportedException()
        };
        return Math.Clamp(double.IsFinite(value) ? value : -distance > 0d ? 1d : 0d, 0d, 1d);
    }

    private string ShapeMarker()
    {
        var json = JsonSerializer.Serialize(new PlanMarker(_plan.Name, _plan.Metric.ToString(), _plan.Model));
        return Infrastructure.Constants.Schema.ShapeMarkerPrefix +
               Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static bool TryReadMarker(string comment, out PlanMarker marker)
    {
        marker = null!;
        if (!comment.StartsWith(Infrastructure.Constants.Schema.ShapeMarkerPrefix, StringComparison.Ordinal))
            return false;
        try
        {
            var encoded = comment[Infrastructure.Constants.Schema.ShapeMarkerPrefix.Length..];
            marker = JsonSerializer.Deserialize<PlanMarker>(Convert.FromBase64String(encoded))!;
            return marker is not null;
        }
        catch (Exception error) when (error is FormatException or JsonException)
        {
            return false;
        }
    }

    private static string Scope(VectorScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Values.Properties.Count == 0) return scope.Identity;
        var values = VectorMetadata.ToJson(scope.Values) ?? string.Empty;
        var payload = scope.Identity + "\u001f" + values;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string ToVectorLiteral(ReadOnlySpan<float> embedding)
    {
        var builder = new StringBuilder(embedding.Length * 12 + 2).Append('[');
        for (var index = 0; index < embedding.Length; index++)
        {
            if (index > 0) builder.Append(',');
            builder.Append(embedding[index].ToString("R", CultureInfo.InvariantCulture));
        }
        return builder.Append(']').ToString();
    }

    private ReadOnlyMemory<float> ParseVector(string value)
    {
        if (value.Length < 2 || value[0] != '[' || value[^1] != ']')
            throw new InvalidOperationException("PgVector returned an invalid vector representation.");
        var parts = value[1..^1].Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != _plan.Dimensions)
            throw new InvalidOperationException(
                $"PgVector returned {parts.Length} dimensions; space '{_plan.Name}' requires {_plan.Dimensions}.");
        var result = new float[parts.Length];
        for (var index = 0; index < parts.Length; index++)
            if (!float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out result[index]) ||
                !float.IsFinite(result[index]))
                throw new InvalidOperationException("PgVector returned a non-finite or malformed vector value.");
        return result;
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
                ?? throw new InvalidOperationException(
                    $"Vector identity '{value}' could not be converted to '{typeof(TKey).FullName}'."));
        return (TKey)Convert.ChangeType(value, typeof(TKey), CultureInfo.InvariantCulture);
    }

    private void DemandBatch(int count)
    {
        if (count > _options.MaxBatchPoints)
            throw new ArgumentOutOfRangeException(nameof(count),
                $"PgVector batch contains {count} points; configured maximum is {_options.MaxBatchPoints}.");
    }

    private static void DemandUnique(IEnumerable<string> keys, string operation)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
            if (!seen.Add(key))
                throw new ArgumentException(
                    $"PgVector batch {operation} contains duplicate identity '{key}'. " +
                    "Submit each identity once so every ordered outcome is unambiguous.",
                    nameof(keys));
    }

    private VectorSearchResult<TKey> EmptyResult() => new(
        [],
        null,
        new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Exact, null));

    private static BatchResult<TKey> Missing(IReadOnlyList<TKey> ids) => new(
        ids.Select((id, index) => new BatchItemResult<TKey>(index, id, MutationOutcome.Missing)),
        BatchAtomicity.NotGuaranteed);

    private InvalidOperationException WrongShape(string table, string reason) => new(
        $"PgVector table '{table}' cannot realize space '{_plan.Name}': {reason}. " +
        "Provision the declared shape or select the source that owns this table.");

    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record PreparedPoint(
        VectorPoint<TKey> Point,
        string Key,
        string Embedding,
        string? Metadata,
        string? FilterData);

    private sealed record NativeShape(
        string? EmbeddingType,
        string? Comment,
        string? IdType,
        string? ScopeType,
        string? MetadataType,
        string? FilterDataType,
        string? PrimaryKey,
        bool? EmbeddingNotNull,
        bool? IdNotNull,
        bool? ScopeNotNull,
        bool? MetadataNotNull,
        bool? FilterDataNotNull,
        bool? PrimaryKeyDeferrable,
        string? ExtraRequiredColumns);
    private sealed record PlanMarker(string Space, string Metric, string? Model);
}
