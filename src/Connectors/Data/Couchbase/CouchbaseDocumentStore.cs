using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Couchbase;
using Couchbase.KeyValue;
using Couchbase.Management.Collections;
using Couchbase.Query;
using Couchbase.Transactions;
using Couchbase.Core.Exceptions.KeyValue;
using Couchbase.Transactions.Error;
using Koan.Core.Adapters;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Connector.Couchbase.Infrastructure;
using Koan.Data.Core;
using Koan.Data.Core.Configuration;
using Koan.Data.Core.Document;
using Koan.Data.Core.Optimization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using KvDurabilityLevel = Couchbase.KeyValue.DurabilityLevel;

namespace Koan.Data.Connector.Couchbase;

/// <summary>
/// The Couchbase document adapter — folds onto the <see cref="DocumentStore{TEntity,TKey}"/> family base (ARCH-0103 P4 /
/// the document-store catalogue), the last document-family adapter to do so. The base owns the readiness-gated + traced
/// op-template, the AODB managed-write composition, the schema-ready gate, and the batch / instruction skeletons; this
/// dialect supplies only the native Couchbase primitives over <see cref="ICouchbaseCollection"/> + N1QL and announces its
/// native extras (bulk · atomic · CAS · raw N1QL).
///
/// <para><b>Harvested intact</b> (not rewritten — these encode hard-won correctness): the
/// <see cref="CouchbaseN1qlFilterTranslator"/> (enum-as-int / GUID-N / null semantics — caught by the FilterConvergence
/// oracle), the GUID-N key + storage encoding (<see cref="GetKey"/> / <see cref="PrepareEntityForStorage"/>), the
/// CAS-based conditional replace, the transaction batch, the primary-index online-wait, and the RequestPlus read
/// consistency.</para>
///
/// <para><b>The three AODB modes</b> map to Couchbase's native 3-level keyspace: <b>Shared</b> (FieldFilter) stamps the
/// framework-managed discriminator into the document JSON (<see cref="ManagedFieldJsonInjector"/>) and conflict-guards the
/// write through a CAS loop; <b>Container</b> (Particle) routes each ambient partition to a distinct native <b>scope</b>
/// (<see cref="CouchbaseClusterProvider.GetCollectionContext"/>); <b>Database</b> (Moniker) routes each source to a
/// distinct native <b>bucket</b> (a per-source provider, pooled by <see cref="CouchbaseAdapterFactory"/>).</para>
/// </summary>
internal sealed class CouchbaseDocumentStore<TEntity, TKey> :
    DocumentStore<TEntity, TKey>,
    IConditionalWriteRepository<TEntity, TKey>,
    IRawQueryRepository<TEntity, TKey>,
    IBulkUpsert<TKey>,
    IBulkDelete<TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    // The document-JSON serializer for the managed-write path: camelCase property names (matching the Couchbase SDK's
    // DefaultSerializer, the same Newtonsoft engine) so the injected JObject's fields line up with the N1QL field
    // expressions the translator emits AND deserialize back through the SDK on read. Only the managed path builds a
    // JObject; the plain path stores the POCO directly (byte-identical to pre-fold).
    private static readonly JsonSerializer DocSerializer =
        JsonSerializer.Create(new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });

    private readonly CouchbaseClusterProvider _provider;
    private readonly IOptionsMonitor<CouchbaseOptions> _options;
    private readonly IServiceProvider _sp;
    private readonly string _source;
    private readonly ILogger? _logger;
    private readonly StorageOptimizationInfo _optimizationInfo;
    private readonly KvDurabilityLevel? _kvDurability;

    public CouchbaseDocumentStore(CouchbaseClusterProvider provider, IOptionsMonitor<CouchbaseOptions> options, IServiceProvider sp, string source)
    {
        _provider = provider;
        _options = options;
        _sp = sp;
        _source = source;
        _logger = sp.GetService<ILogger<CouchbaseDocumentStore<TEntity, TKey>>>();
        _optimizationInfo = sp.GetStorageOptimization<TEntity, TKey>();
        if (!string.IsNullOrWhiteSpace(_options.CurrentValue.DurabilityLevel) &&
            Enum.TryParse<KvDurabilityLevel>(_options.CurrentValue.DurabilityLevel, true, out var kv))
        {
            _kvDurability = kv;
        }
    }

    public StorageOptimizationInfo OptimizationInfo => _optimizationInfo;

    // ==================== The family seam: identity, telemetry, readiness ====================

    protected override IAdapterReadiness Readiness => _provider;
    protected override ActivitySource Telemetry => CouchbaseTelemetry.Activity;
    protected override string Verb => "couchbase";
    protected override string? RoutedSource => string.Equals(_source, "Default", StringComparison.OrdinalIgnoreCase) ? null : _source;

    public override ReadinessPolicy Policy => _options.CurrentValue.Readiness.Policy;
    public override TimeSpan Timeout
    {
        get { var t = _options.CurrentValue.Readiness.Timeout; return t > TimeSpan.Zero ? t : _provider.ReadinessTimeout; }
    }
    public override bool EnableReadinessGating => _options.CurrentValue.Readiness.EnableReadinessGating;

    protected override void DescribeBackend(ICapabilities caps) => caps
        .Add(DataCaps.Query.String)
        .Add(DataCaps.Write.BulkUpsert).Add(DataCaps.Write.BulkDelete)
        .Add(DataCaps.Write.AtomicBatch)
        .Add(DataCaps.Write.ConditionalReplace)   // native CAS (DATA-0102)
        .Add(DataCaps.Query.Filter, CouchbaseN1qlFilterTranslator.Capabilities);

    // ==================== Container resolution + schema (Container mode) ====================

    // Sanitize the framework-resolved name to Couchbase's collection charset (the resolver leaves a nested-type '+'
    // intact, which the collection manager rejects). Applied at the single resolution point so create + query agree.
    private string CollectionName() => CouchbaseAdapterFactory.FormatCollectionName(AdapterNaming.GetOrCompute<TEntity, TKey>(_sp));

    // The schema-ready gate keys on the full physical container — source (bucket placement) · bucket · scope (ambient
    // partition) · collection — so each partition's scope and each routed source's bucket get their primary index
    // ensured exactly once.
    private static string SchemaKey(CouchbaseCollectionContext ctx)
        => $"{ctx.BucketName}|{ctx.ScopeName}|{ctx.CollectionName}";

    // Resolve the current physical container (connects the provider, maps the ambient partition onto the scope, creates
    // the scope/collection if missing) AND ensures its primary index once. Every native op funnels through here, so the
    // schema is provisioned on the first touch of each (bucket, scope, collection). UNGATED on readiness: resolving the
    // context is what CONNECTS the provider (Initializing→Ready), so it cannot wait on readiness first.
    private async ValueTask<CouchbaseCollectionContext> GetContextAsync(CancellationToken ct)
    {
        var ctx = await _provider.GetCollectionContext(CollectionName(), ct).ConfigureAwait(false);
        await Schema.RunOnceAsync(SchemaKey(ctx), () => EnsureSchemaAsync(ctx, ct), ct).ConfigureAwait(false);
        return ctx;
    }

    // EnsureContainerAsync (the base seam) IS the schema provisioner the facade calls before every op and an explicit
    // EnsureCreated invokes. Resolving the context already creates the scope/collection; the gate adds the primary index.
    protected override async Task EnsureContainerAsync(CancellationToken ct) => await GetContextAsync(ct).ConfigureAwait(false);

    private async Task EnsureSchemaAsync(CouchbaseCollectionContext ctx, CancellationToken ct)
    {
        var manager = ctx.Bucket.Collections;
        if (!string.Equals(ctx.ScopeName, "_default", StringComparison.Ordinal))
        {
            try { await manager.CreateScopeAsync(ctx.ScopeName).ConfigureAwait(false); }
            catch (CouchbaseException ex) when (IsAlreadyExists(ex)) { /* raced — fine */ }
        }
        try
        {
            await manager.CreateCollectionAsync(ctx.ScopeName, ctx.CollectionName, new CreateCollectionSettings()).ConfigureAwait(false);
            await Task.Delay(2000, ct).ConfigureAwait(false);   // let the collection register for N1QL before CREATE INDEX
        }
        catch (CouchbaseException ex) when (IsAlreadyExists(ex)) { /* exists — fine */ }

        // The server only auto-creates the primary index on the bucket's _default collection — every named collection
        // (or custom-named bucket default) needs an explicit CREATE PRIMARY INDEX so N1QL queries don't fail with
        // "No index available". Idempotent via the already-exists (4300) catch.
        await EnsurePrimaryIndexAsync(ctx, ct).ConfigureAwait(false);
    }

    private async Task EnsurePrimaryIndexAsync(CouchbaseCollectionContext ctx, CancellationToken ct)
    {
        var keyspace = FullKeyspace(ctx);
        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                await ctx.Cluster.QueryAsync<dynamic>($"CREATE PRIMARY INDEX ON {keyspace}").ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("4300"))
            {
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts - 1)
            {
                _logger?.LogDebug(ex, "CREATE PRIMARY INDEX attempt {Attempt}/{Max} failed on {Keyspace}, retrying", attempt + 1, maxAttempts, keyspace);
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }

        // Wait for the index to come online before returning so the first query doesn't race the indexer warm-up.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                var probe = await ctx.Cluster.QueryAsync<string>(
                    "SELECT RAW state FROM system:indexes WHERE bucket_id = $bucket AND scope_id = $scope AND keyspace_id = $collection AND is_primary = true",
                    options => options.Parameter("bucket", ctx.BucketName).Parameter("scope", ctx.ScopeName).Parameter("collection", ctx.CollectionName)).ConfigureAwait(false);
                var states = new List<string>();
                await foreach (var row in probe.ConfigureAwait(false)) states.Add(row ?? "");
                if (states.Count > 0 && states.All(s => string.Equals(s, "online", StringComparison.OrdinalIgnoreCase))) return;
            }
            catch { /* system catalog may transiently fail during indexer warm-up */ }
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        _logger?.LogWarning("Primary index on {Keyspace} did not report online; subsequent queries may fail", keyspace);
    }

    // ==================== Read ====================

    protected override async Task<TEntity?> FindByIdAsync(TKey id, CancellationToken ct)
    {
        var ctx = await GetContextAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await ctx.Collection.GetAsync(GetKey(id), new GetOptions().CancellationToken(ct)).ConfigureAwait(false);
            return result.ContentAs<TEntity>();
        }
        catch (DocumentNotFoundException) { return null; }
    }

    protected override async Task<IReadOnlyList<TEntity?>> FindManyAsync(IReadOnlyList<TKey> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var ctx = await GetContextAsync(ct).ConfigureAwait(false);
        var results = new TEntity?[ids.Count];
        var gets = ids.Select(id => GetOne(ctx, id, ct)).ToArray();
        var fetched = await Task.WhenAll(gets).ConfigureAwait(false);
        for (var i = 0; i < ids.Count; i++) results[i] = fetched[i];
        return results;

        static async Task<TEntity?> GetOne(CouchbaseCollectionContext ctx, TKey id, CancellationToken ct)
        {
            try
            {
                var r = await ctx.Collection.GetAsync(GetKey(id), new GetOptions().CancellationToken(ct)).ConfigureAwait(false);
                return r.ContentAs<TEntity>();
            }
            catch (DocumentNotFoundException) { return null; }
        }
    }

    /// <summary>
    /// Translate the WHOLE (guaranteed-pushable) filter to a parameterized N1QL WHERE, push sort + pagination
    /// natively, and report what we handled. Never falls back, never re-throws translation failures.
    /// </summary>
    protected override async Task<RepositoryQueryResult<TEntity>> QueryNativeAsync(QueryDefinition query, CancellationToken ct)
    {
        var ctx = await GetContextAsync(ct).ConfigureAwait(false);
        var keyspace = FullKeyspace(ctx);

        var (where, parameters) = TranslateFilter(query.Filter);
        var orderBy = BuildOrderBy(query.Sort, out var sortHandled);

        var sb = new System.Text.StringBuilder();
        sb.Append("SELECT RAW doc FROM ").Append(keyspace).Append(" AS doc");
        if (where is not null) sb.Append(" WHERE ").Append(where);
        if (orderBy is not null) sb.Append(" ORDER BY ").Append(orderBy);

        // Only push pagination when the sort was fully pushed down — a deep/collection sort is finished by the
        // coordinator's in-memory sorter, which needs the full matching set (filter residual already strips pagination).
        var sortFullyHandled = query.Sort is null || query.Sort.Count == 0 || sortHandled.Count == query.Sort.Count;
        var paginationHandled = false;
        if (query.HasPagination && sortFullyHandled)
        {
            var size = query.EffectivePageSize();
            var offset = (query.EffectivePage() - 1) * size;
            sb.Append(" LIMIT ").Append(size.ToString(CultureInfo.InvariantCulture)).Append(" OFFSET ").Append(offset.ToString(CultureInfo.InvariantCulture));
            paginationHandled = true;
        }

        var statement = sb.ToString();
        var definition = parameters is null ? null : new CouchbaseQueryDefinition(statement) { Parameters = parameters };
        var items = await ExecuteQuery(ctx, statement, definition, ct).ConfigureAwait(false);

        return new RepositoryQueryResult<TEntity>
        {
            Items = items,
            PaginationHandled = paginationHandled,
            SortHandled = sortHandled,
        };
    }

    protected override async Task<CountResult> CountNativeAsync(QueryDefinition query, CancellationToken ct)
    {
        var ctx = await GetContextAsync(ct).ConfigureAwait(false);
        var keyspace = FullKeyspace(ctx);
        var (where, parameters) = TranslateFilter(query.Filter);
        var statement = where is null
            ? $"SELECT RAW COUNT(*) FROM {keyspace}"
            : $"SELECT RAW COUNT(*) FROM {keyspace} AS doc WHERE {where}";
        var definition = parameters is null ? null : new CouchbaseQueryDefinition(statement) { Parameters = parameters };
        var result = await ExecuteScalarQueryAsync<long>(ctx, statement, definition, ct).ConfigureAwait(false);
        return CountResult.Exact(result);
    }

    // ==================== Write (Shared-mode managed composition) ====================

    protected override async Task UpsertOneNativeAsync(TEntity model, IReadOnlyDictionary<string, object?>? inject, IReadOnlyDictionary<string, object?>? guard, CancellationToken ct)
    {
        var ctx = await GetContextAsync(ct).ConfigureAwait(false);
        PrepareEntityForStorage(model);
        var key = GetKey(model.Id);
        if (inject is null || inject.Count == 0)
        {
            await ctx.Collection.UpsertAsync(key, model, UpsertOpts(ct)).ConfigureAwait(false);   // byte-identical plain path
            return;
        }
        await ManagedUpsertOneAsync(ctx, key, model, inject, guard, ct).ConfigureAwait(false);
    }

    protected override async Task<int> UpsertManyNativeAsync(IReadOnlyList<TEntity> models, IReadOnlyDictionary<string, object?>? inject, IReadOnlyDictionary<string, object?>? guard, CancellationToken ct)
    {
        if (models.Count == 0) return 0;
        var ctx = await GetContextAsync(ct).ConfigureAwait(false);

        if (inject is not null && inject.Count > 0)
        {
            // Under a managed scope, write per document through the conflict-aware path (inject Effective, guard Current).
            foreach (var model in models)
            {
                ct.ThrowIfCancellationRequested();
                PrepareEntityForStorage(model);
                await ManagedUpsertOneAsync(ctx, GetKey(model.Id), model, inject, guard, ct).ConfigureAwait(false);
            }
            return models.Count;
        }

        var upserts = new List<Task>(models.Count);
        foreach (var model in models)
        {
            ct.ThrowIfCancellationRequested();
            PrepareEntityForStorage(model);
            upserts.Add(ctx.Collection.UpsertAsync(GetKey(model.Id), model, UpsertOpts(ct)));
        }
        await Task.WhenAll(upserts).ConfigureAwait(false);
        return models.Count;
    }

    // The Shared-mode CAS retry bound. Each attempt re-reads + re-guards, so normal concurrent same-scope writes
    // serialize and succeed within a few attempts; the bound only trips under pathological same-id contention.
    private const int ManagedUpsertMaxAttempts = 32;

    // Managed-field conflict-aware upsert (DATA-0105 §3b; the Couchbase realization of the Mongo E11000 guard). Serialize
    // the model to camelCase JSON, inject the managed elements (Effective), then write under a CAS loop: read the current
    // doc (capturing its CAS); if a managed isolation guard (Current) no longer holds, the doc is owned by another scope →
    // reject; otherwise Replace under the captured CAS. A missing doc → Insert. A concurrent insert/replace re-runs the
    // loop so the guard is ALWAYS evaluated against the live doc — same-scope contention is last-writer-wins (matching the
    // golden Mongo atomic filtered replace), cross-scope is rejected.
    //
    // The injected values AND the guard comparands are normalized through the SAME on-disk form the N1QL read filter uses
    // (Guid→"N", enum→numeric, Unspecified DateTime→UTC) so a Guid/enum/DateTime-typed managed field stamps the exact bytes
    // a scoped read queries — otherwise the write (Newtonsoft default, e.g. dashed Guid) and the read (dashless "N") would
    // disagree and a scoped read would never match its own rows. Strings (the shipped __koan_tenant) pass through unchanged.
    //
    // On exhaustion the guard-checked write is NOT abandoned to an unconditional Upsert: a doc can transition scope between
    // a guard-check and an unguarded write (delete → foreign insert), so an unconditional fallback would be a cross-scope
    // leak. Instead the bound trips a clear framework contention error (fail-closed; the caller retries).
    private async Task ManagedUpsertOneAsync(CouchbaseCollectionContext ctx, string key, TEntity model, IReadOnlyDictionary<string, object?> inject, IReadOnlyDictionary<string, object?>? guard, CancellationToken ct)
    {
        var doc = JObject.FromObject(model, DocSerializer);
        ManagedFieldJsonInjector.InjectManaged(doc, Normalize(inject));
        var normalizedGuard = guard is { Count: > 0 } ? Normalize(guard) : null;
        var collection = ctx.Collection;

        for (var attempt = 0; attempt < ManagedUpsertMaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var existing = await collection.GetAsync(key, new GetOptions().CancellationToken(ct)).ConfigureAwait(false);
                if (normalizedGuard is not null && !GuardMatches(existing.ContentAs<JObject>() ?? new JObject(), normalizedGuard))
                    throw CrossScopeWrite(ctx.CollectionName, key);
                var replace = new ReplaceOptions().Cas(existing.Cas).CancellationToken(ct);
                if (_kvDurability is { } d) replace.Durability(d);
                await collection.ReplaceAsync(key, doc, replace).ConfigureAwait(false);
                return;
            }
            catch (DocumentNotFoundException)
            {
                try
                {
                    var insert = new InsertOptions().CancellationToken(ct);
                    if (_kvDurability is { } d) insert.Durability(d);
                    await collection.InsertAsync(key, doc, insert).ConfigureAwait(false);
                    return;
                }
                catch (DocumentExistsException)
                {
                    // a concurrent writer inserted between our Get and Insert — loop to re-evaluate the guard against it
                }
            }
            catch (global::Couchbase.Core.Exceptions.CasMismatchException)
            {
                // a concurrent writer replaced between our Get and the CAS-guarded Replace — loop to re-read + re-guard
            }
        }
        throw new InvalidOperationException(
            $"Exceeded {ManagedUpsertMaxAttempts} compare-and-swap attempts writing id '{key}' to '{ctx.CollectionName}' under " +
            "sustained concurrent same-scope contention; the write was not applied (fail-closed — retry).");
    }

    // Normalize each managed value to the on-disk form the N1QL read filter uses, so write-stamp ⇄ read-filter ⇄
    // write-guard all agree for Guid/enum/DateTime-typed managed fields (strings pass through unchanged).
    private static IReadOnlyDictionary<string, object?> Normalize(IReadOnlyDictionary<string, object?> values)
    {
        var d = new Dictionary<string, object?>(values.Count, StringComparer.Ordinal);
        foreach (var kv in values) d[kv.Key] = CouchbaseN1qlFilterTranslator.NormalizeValue(kv.Value);
        return d;
    }

    private static bool GuardMatches(JObject existing, IReadOnlyDictionary<string, object?> guard)
    {
        foreach (var kv in guard)
        {
            if (!existing.TryGetValue(kv.Key, out var token)) return false;   // foreign / unscoped doc — reject
            var expected = kv.Value is null ? JValue.CreateNull() : JToken.FromObject(kv.Value);
            if (!JToken.DeepEquals(token, expected)) return false;
        }
        return true;
    }

    protected override async Task<bool> DeleteOneNativeAsync(TKey id, CancellationToken ct)
    {
        var ctx = await GetContextAsync(ct).ConfigureAwait(false);
        return await RemoveOne(ctx.Collection, GetKey(id), ct).ConfigureAwait(false);
    }

    protected override async Task<int> DeleteManyNativeAsync(IReadOnlyList<TKey> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return 0;
        var ctx = await GetContextAsync(ct).ConfigureAwait(false);
        var tasks = new List<Task<bool>>(ids.Count);
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            tasks.Add(RemoveOne(ctx.Collection, GetKey(id), ct));
        }
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Count(static x => x);
    }

    protected override async Task<long> ClearNativeAsync(RemoveStrategy strategy, CancellationToken ct)
    {
        // No native fast path (bucket flush requires admin) — this adapter does not declare FastRemove, so every strategy
        // is the safe N1QL delete over the current keyspace.
        var ctx = await GetContextAsync(ct).ConfigureAwait(false);
        var statement = $"DELETE FROM {FullKeyspace(ctx)} RETURNING META().id";
        var count = 0L;
        await foreach (var _ in ExecuteQueryAsync<dynamic>(ctx, statement, null, ct).ConfigureAwait(false)) count++;
        return count;
    }

    protected override async Task<BatchResult> SaveBatchNativeAsync(IReadOnlyList<TEntity> upserts, IReadOnlyList<TKey> deletes, bool requireAtomic, CancellationToken ct)
    {
        var ctx = await GetContextAsync(ct).ConfigureAwait(false);
        if (requireAtomic) return await SaveBatchAtomicAsync(ctx, upserts, deletes, ct).ConfigureAwait(false);

        var upTasks = new List<Task>(upserts.Count);
        foreach (var e in upserts)
        {
            ct.ThrowIfCancellationRequested();
            PrepareEntityForStorage(e);
            upTasks.Add(ctx.Collection.UpsertAsync(GetKey(e.Id), e, UpsertOpts(ct)));
        }
        await Task.WhenAll(upTasks).ConfigureAwait(false);

        var delTasks = new List<Task<bool>>(deletes.Count);
        foreach (var id in deletes)
        {
            ct.ThrowIfCancellationRequested();
            delTasks.Add(RemoveOne(ctx.Collection, GetKey(id), ct));
        }
        var deleted = (await Task.WhenAll(delTasks).ConfigureAwait(false)).Count(static x => x);
        return new BatchResult(upserts.Count, 0, deleted);
    }

    // RequireAtomic → a single Couchbase transaction (I18: a deployment that cannot run it throws, never silently
    // non-atomic). Upserts apply before deletes (the family batch contract).
    private async Task<BatchResult> SaveBatchAtomicAsync(CouchbaseCollectionContext ctx, IReadOnlyList<TEntity> upserts, IReadOnlyList<TKey> deletes, CancellationToken ct)
    {
        var added = 0; var updated = 0; var deleted = 0;
        try
        {
            await ctx.Cluster.Transactions.RunAsync(async attempt =>
            {
                added = 0; updated = 0; deleted = 0;   // reset (the lambda may re-run on transaction retry)
                foreach (var entity in upserts)
                {
                    PrepareEntityForStorage(entity);
                    var key = GetKey(entity.Id);
                    try
                    {
                        var existing = await attempt.GetAsync(ctx.Collection, key).ConfigureAwait(false);
                        await attempt.ReplaceAsync(existing, entity).ConfigureAwait(false);
                        updated++;
                    }
                    catch (TransactionFailedException ex) when (ex.InnerException is DocumentNotFoundException)
                    {
                        await attempt.InsertAsync(ctx.Collection, key, entity).ConfigureAwait(false);
                        added++;
                    }
                }
                foreach (var id in deletes)
                {
                    try
                    {
                        var current = await attempt.GetAsync(ctx.Collection, GetKey(id)).ConfigureAwait(false);
                        await attempt.RemoveAsync(current).ConfigureAwait(false);
                        deleted++;
                    }
                    catch (TransactionFailedException ex) when (ex.InnerException is DocumentNotFoundException) { /* already gone */ }
                }
            }).ConfigureAwait(false);
        }
        catch (TransactionFailedException ex)
        {
            throw new NotSupportedException("Couchbase cluster failed to execute the atomic batch transaction.", ex);
        }
        return new BatchResult(added, updated, deleted);
    }

    // ==================== Native CAS (IConditionalWriteRepository) ====================

    /// <summary>
    /// Atomic CAS (JOBS-0005 §20.3 / DATA-0102): Get the document (capturing its CAS), evaluate the guard against the
    /// current content, then Replace under that CAS. <c>true</c> if applied; <c>false</c> if the guard no longer held,
    /// the document is gone, or another writer won the race (CAS mismatch).
    /// </summary>
    public Task<bool> ConditionalReplaceAsync(TEntity model, Expression<Func<TEntity, bool>> guard, CancellationToken ct = default)
        => RunAsync("conditional-replace", async () =>
        {
            var ctx = await GetContextAsync(ct).ConfigureAwait(false);
            PrepareEntityForStorage(model);   // before deriving the key so it matches how Upsert stored the doc
            var key = GetKey(model.Id);
            var predicate = guard.Compile();
            try
            {
                var existing = await ctx.Collection.GetAsync(key, new GetOptions().CancellationToken(ct)).ConfigureAwait(false);
                if (existing.ContentAs<TEntity>() is not { } current || !predicate(current)) return false;
                var replace = new ReplaceOptions().Cas(existing.Cas).CancellationToken(ct);
                if (_kvDurability is { } d) replace.Durability(d);
                await ctx.Collection.ReplaceAsync(key, model, replace).ConfigureAwait(false);
                return true;
            }
            catch (DocumentNotFoundException) { return false; }
            catch (global::Couchbase.Core.Exceptions.CasMismatchException) { return false; }
        }, ct);

    // ==================== Raw N1QL escape hatch (IRawQueryRepository → DataCaps.Query.String) ====================

    public Task<RepositoryQueryResult<TEntity>> QueryRaw(string query, object? parameters, QueryDefinition shaping, CancellationToken ct = default)
        => RunAsync("query.raw", async () =>
        {
            var ctx = await GetContextAsync(ct).ConfigureAwait(false);
            var sb = new System.Text.StringBuilder(query);
            var paginationHandled = false;
            if (shaping.HasPagination)
            {
                var size = shaping.EffectivePageSize();
                var offset = (shaping.EffectivePage() - 1) * size;
                sb.Append(" LIMIT ").Append(size.ToString(CultureInfo.InvariantCulture)).Append(" OFFSET ").Append(offset.ToString(CultureInfo.InvariantCulture));
                paginationHandled = true;
            }
            var definition = BuildRawDefinition(sb.ToString(), parameters);
            var items = await ExecuteQuery(ctx, definition.Statement, definition, ct).ConfigureAwait(false);
            return new RepositoryQueryResult<TEntity>
            {
                Items = items,
                PaginationHandled = paginationHandled,
                SortHandled = RepositoryQueryResult<TEntity>.NoSortHandled,
            };
        }, ct);

    public Task<CountResult> CountRaw(string query, object? parameters, CancellationToken ct = default)
        => RunAsync("count.raw", async () =>
        {
            var ctx = await GetContextAsync(ct).ConfigureAwait(false);
            var inner = BuildRawDefinition(query, parameters);   // wrap so COUNT(*) works regardless of the caller's projection
            var statement = $"SELECT RAW COUNT(*) FROM ({inner.Statement}) AS sub";
            var definition = new CouchbaseQueryDefinition(statement) { Parameters = inner.Parameters };
            var result = await ExecuteScalarQueryAsync<long>(ctx, statement, definition, ct).ConfigureAwait(false);
            return CountResult.Exact(result);
        }, ct);

    // ==================== Filter / sort translation (harvested intact) ====================

    private (string? Where, IDictionary<string, object?>? Parameters) TranslateFilter(Filter? filter)
    {
        if (filter is null) return (null, null);
        var translation = CouchbaseN1qlFilterTranslator.Translate(filter, typeof(TEntity), _optimizationInfo);
        return (translation.WhereClause, translation.Parameters.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value));
    }

    private string? BuildOrderBy(IReadOnlyList<SortSpec> specs, out IReadOnlySet<SortSpec> handled)
    {
        handled = RepositoryQueryResult<TEntity>.NoSortHandled;
        if (specs is null || specs.Count == 0) return null;

        var handledSet = new HashSet<SortSpec>();
        var sb = new System.Text.StringBuilder();
        foreach (var spec in specs)
        {
            if (spec.Path.TraversesCollection) continue;   // leave collection sort to the floor
            var field = SortFieldExpression(spec.Path);
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(field).Append(spec.Desc ? " DESC" : " ASC");
            handledSet.Add(spec);
        }
        if (handledSet.Count == 0) return null;
        handled = handledSet.ToFrozenSet();
        return sb.ToString();
    }

    private string SortFieldExpression(MemberPath path)
    {
        if (path.Members.Count == 1 && IsIdMember(path.Members[0].Name)) return "META().id";
        var sb = new System.Text.StringBuilder("doc");
        foreach (var member in path.Members) sb.Append('.').Append(QuoteIdentifier(NormalizeProperty(member.Name)));
        return sb.ToString();
    }

    private bool IsIdMember(string memberName)
        => string.Equals(memberName, _optimizationInfo.IdPropertyName, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(memberName, "Id", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeProperty(string property)
        => property.Length == 0 ? property : property[..1].ToLowerInvariant() + property[1..];

    private static string FullKeyspace(CouchbaseCollectionContext ctx)
        => $"{QuoteIdentifier(ctx.BucketName)}.{QuoteIdentifier(ctx.ScopeName)}.{QuoteIdentifier(ctx.CollectionName)}";

    private static string QuoteIdentifier(string name) => "`" + name.Replace("`", "``") + "`";

    private static CouchbaseQueryDefinition BuildRawDefinition(string statement, object? parameters)
    {
        IDictionary<string, object?>? dict = parameters switch
        {
            null => null,
            IDictionary<string, object?> d => d,
            CouchbaseQueryDefinition def => def.Parameters,
            _ => ToParameterDictionary(parameters)
        };
        return new CouchbaseQueryDefinition(statement) { Parameters = dict };
    }

    private static IDictionary<string, object?> ToParameterDictionary(object parameters)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in parameters.GetType().GetProperties())
        {
            var name = prop.Name.StartsWith('$') ? prop.Name : "$" + prop.Name;
            dict[name] = prop.GetValue(parameters);
        }
        return dict;
    }

    // ==================== N1QL execution (harvested intact) ====================

    private async Task<IReadOnlyList<TEntity>> ExecuteQuery(CouchbaseCollectionContext ctx, string statement, CouchbaseQueryDefinition? definition, CancellationToken ct)
    {
        var rows = new List<TEntity>();
        await foreach (var row in ExecuteQueryAsync<TEntity>(ctx, statement, definition, ct).ConfigureAwait(false)) rows.Add(row);
        return rows;
    }

    private async Task<T> ExecuteScalarQueryAsync<T>(CouchbaseCollectionContext ctx, string statement, CouchbaseQueryDefinition? definition, CancellationToken ct)
    {
        await foreach (var row in ExecuteQueryAsync<T>(ctx, statement, definition, ct).ConfigureAwait(false)) return row;
        return default!;
    }

    private async IAsyncEnumerable<T> ExecuteQueryAsync<T>(CouchbaseCollectionContext ctx, string statement, CouchbaseQueryDefinition? definition, [EnumeratorCancellation] CancellationToken ct)
    {
        var queryOptions = new QueryOptions();
        var timeout = definition?.Timeout ?? _options.CurrentValue.QueryTimeout;
        if (timeout > TimeSpan.Zero) queryOptions.Timeout(timeout);
        queryOptions.CancellationToken(ct);
        // N1QL defaults to not_bounded scan consistency — a query can return stale results right after a mutation.
        // RequestPlus blocks until the indexer caught up with the latest write (read-after-write the controllers + tests need).
        queryOptions.ScanConsistency(QueryScanConsistency.RequestPlus);
        if (definition?.Parameters is { Count: > 0 })
            foreach (var parameter in definition.Parameters)
                queryOptions.Parameter(parameter.Key, parameter.Value ?? DBNull.Value);

        global::Couchbase.Query.IQueryResult<T> result;
        try
        {
            result = await ctx.Cluster.QueryAsync<T>(statement, queryOptions).ConfigureAwait(false);
        }
        catch (global::Couchbase.Core.Exceptions.IndexFailureException ex) when (ex.Message.Contains("Keyspace not found"))
        {
            await EnsureSchemaAsync(ctx, ct).ConfigureAwait(false);   // the collection registered late — ensure + retry once
            result = await ctx.Cluster.QueryAsync<T>(statement, queryOptions).ConfigureAwait(false);
        }

        await foreach (var row in result.ConfigureAwait(false)) yield return row;
    }

    // ==================== Key + storage encoding (harvested intact — GUID-N single source of truth) ====================

    private async Task<bool> RemoveOne(ICouchbaseCollection collection, string key, CancellationToken ct)
    {
        try
        {
            var options = new RemoveOptions().CancellationToken(ct);
            if (_kvDurability is { } d) options.Durability(d);
            await collection.RemoveAsync(key, options).ConfigureAwait(false);
            return true;
        }
        catch (DocumentNotFoundException) { return false; }
    }

    private UpsertOptions UpsertOpts(CancellationToken ct)
    {
        var options = new UpsertOptions().CancellationToken(ct);
        if (_kvDurability is { } d) options.Durability(d);
        return options;
    }

    private void PrepareEntityForStorage(TEntity entity)
    {
        if (!_optimizationInfo.IsOptimized || typeof(TKey) != typeof(string) || string.IsNullOrWhiteSpace(_optimizationInfo.IdPropertyName)) return;
        var prop = typeof(TEntity).GetProperty(_optimizationInfo.IdPropertyName);
        if (prop is null || prop.PropertyType != typeof(string)) return;
        if (prop.GetValue(entity) is string value && Guid.TryParse(value, out var guid))
            prop.SetValue(entity, guid.ToString("N", CultureInfo.InvariantCulture));
    }

    private static string GetKey(TKey id) => id switch
    {
        string str => str,
        Guid guid => guid.ToString("N", CultureInfo.InvariantCulture),
        _ => Convert.ToString(id, CultureInfo.InvariantCulture) ?? throw new InvalidOperationException("Unable to convert key to string.")
    };

    private static bool IsAlreadyExists(CouchbaseException ex)
        => ex.Context?.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true;
}
