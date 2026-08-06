using Koan.Core.Capabilities;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector;

/// <summary>Entity-centered vector persistence and similarity search.</summary>
public class Vector<TEntity> where TEntity : class, IEntity<string>
{
    private static IVectorRuntime Runtime =>
        AppHost.Current?.GetService<IVectorRuntime>()
        ?? throw new InvalidOperationException(
            "Koan Vector is unavailable in the current host. Reference Sylin.Koan.Data.Vector and one connector, then call AddKoan().");

    public static bool IsAvailable
    {
        get
        {
            try
            {
                _ = Runtime.Resolve<TEntity, string>(DataOperationEffect.Read, "vector availability");
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }
    }

    public static IDisposable WithPartition(string partition) => EntityContext.Partition(partition);

    public static Task Save(
        TEntity entity,
        ReadOnlyMemory<float> embedding,
        IReadOnlyDictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Save(entity.Id, embedding.ToArray(), metadata, ct);
    }

    public static Task Save(
        TEntity entity,
        float[] embedding,
        IReadOnlyDictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Save(entity.Id, embedding, metadata, ct);
    }

    public static Task Save(
        string id,
        float[] embedding,
        object? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(embedding);
        var execution = Resolve(DataOperationEffect.Write, "vector save");
        ValidateEmbedding(embedding, execution.Plan, nameof(embedding));
        var point = new VectorPoint<string>(
            id,
            new ReadOnlyMemory<float>(embedding.ToArray()),
            execution.Metadata.Materialize(metadata));
        return execution.Repository.Save(point, VectorScope.Unscoped, ct);
    }

    public static async Task<int> Save(
        (string Id, float[] Embedding, object? Metadata) item,
        CancellationToken ct = default)
    {
        await Save(item.Id, item.Embedding, item.Metadata, ct).ConfigureAwait(false);
        return 1;
    }

    public static async Task<int> Save(
        IEnumerable<(string Id, float[] Embedding, object? Metadata)> items,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var input = items as IReadOnlyList<(string Id, float[] Embedding, object? Metadata)> ?? items.ToArray();
        if (input.Count == 0) return 0;
        var execution = Resolve(DataOperationEffect.Write, "vector batch save");
        var points = new VectorPoint<string>[input.Count];
        for (var index = 0; index < input.Count; index++)
        {
            var item = input[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Id);
            ArgumentNullException.ThrowIfNull(item.Embedding);
            ValidateEmbedding(item.Embedding, execution.Plan, $"items[{index}].Embedding");
            points[index] = new VectorPoint<string>(
                item.Id,
                new ReadOnlyMemory<float>(item.Embedding.ToArray()),
                execution.Metadata.Materialize(item.Metadata));
        }
        var result = await execution.Repository
            .Save(points, VectorScope.Unscoped, ct)
            .ConfigureAwait(false);
        return result.Items.Count;
    }

    public static Task<int> Save(
        IEnumerable<VectorData<TEntity>.VectorEntity> items,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        return Save(items.Select(static item => (
            item.Entity.Id,
            item.Vector.ToArray(),
            (object?)item.Metadata)), ct);
    }

    public static async Task<bool> Delete(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var execution = Resolve(DataOperationEffect.Write, "vector delete");
        return await execution.Repository
            .Delete(id, VectorScope.Unscoped, ct)
            .ConfigureAwait(false);
    }

    public static async Task<int> Delete(IEnumerable<string> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var input = ids as IReadOnlyList<string> ?? ids.ToArray();
        if (input.Count == 0) return 0;
        var execution = Resolve(DataOperationEffect.Write, "vector batch delete");
        var result = await execution.Repository
            .Delete(input, VectorScope.Unscoped, ct)
            .ConfigureAwait(false);
        return result.Items.Count(static item => item.Outcome == MutationOutcome.Deleted);
    }

    public static Task<VectorPoint<string>?> Get(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var execution = Resolve(DataOperationEffect.Read, "vector get");
        return execution.Repository.Get(id, VectorScope.Unscoped, ct);
    }

    public static Task<IReadOnlyList<VectorPoint<string>?>> Get(
        IEnumerable<string> ids,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var input = ids as IReadOnlyList<string> ?? ids.ToArray();
        var execution = Resolve(DataOperationEffect.Read, "vector get many");
        return execution.Repository.Get(input, VectorScope.Unscoped, ct);
    }

    public static Task Clear(CancellationToken ct = default)
    {
        var execution = Resolve(DataOperationEffect.Write, "vector clear");
        return execution.Repository.Clear(VectorScope.Unscoped, ct);
    }

    public static Task Sync(CancellationToken ct = default)
    {
        var execution = Resolve(DataOperationEffect.Read, "vector sync");
        return execution.Repository.Sync(VectorScope.Unscoped, ct);
    }

    public static Task EnsureCreated(CancellationToken ct = default)
    {
        var execution = Resolve(DataOperationEffect.SchemaOrAdmin, "vector ensure created");
        return execution.Repository.VectorEnsureCreated(ct);
    }

    /// <summary>Compatibility alias for <see cref="Clear"/>.</summary>
    public static Task Flush(CancellationToken ct = default) => Clear(ct);

    /// <summary>Compatibility instruction for providers that expose explicit rebuild lifecycle.</summary>
    public static Task<bool> Rebuild(CancellationToken ct = default) => ExecuteInstruction<bool>(
        VectorInstructions.IndexRebuild,
        "vector rebuild",
        DataOperationEffect.SchemaOrAdmin,
        ct);

    /// <summary>Compatibility instruction for providers that expose index statistics.</summary>
    public static Task<int> Stats(CancellationToken ct = default) => ExecuteInstruction<int>(
        VectorInstructions.IndexStats,
        "vector stats",
        DataOperationEffect.Read,
        ct);

    public static CapabilitySet GetCapabilities()
    {
        try
        {
            var execution = Resolve(DataOperationEffect.Read, "vector capabilities");
            return VectorCaps.Describe(execution.Repository, execution.Repository.GetType().Name);
        }
        catch (InvalidOperationException) { return new CapabilitySet(); }
    }

    public static async Task<float[]?> GetEmbedding(string id, CancellationToken ct = default) =>
        (await Get(id, ct).ConfigureAwait(false))?.Embedding.ToArray();

    public static async Task<Dictionary<string, float[]>> GetEmbeddings(
        IEnumerable<string> ids,
        CancellationToken ct = default)
    {
        var points = await Get(ids, ct).ConfigureAwait(false);
        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var point in points)
            if (point is not null) result[point.Id] = point.Embedding.ToArray();
        return result;
    }

    public static Task<VectorSearchResult<string>> Search(
        float[] embedding,
        CancellationToken ct = default) => Search(embedding, static _ => { }, ct);

    public static Task<VectorSearchResult<string>> Search(
        float[] embedding,
        Action<VectorQuery> configure,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentNullException.ThrowIfNull(configure);
        var execution = Resolve(DataOperationEffect.Read, "vector search");
        ValidateEmbedding(embedding, execution.Plan, nameof(embedding));
        var builder = new VectorQuery();
        configure(builder);
        var maxTop = AppHost.Current?.GetService<IOptions<VectorDefaultsOptions>>()?.Value.MaxTop
            ?? Infrastructure.Constants.Defaults.MaxTop;
        var request = builder.Build(
            new ReadOnlyMemory<float>(embedding.ToArray()),
            execution.Plan,
            maxTop);
        ValidateQuery(request, execution.Repository);
        return execution.Repository.Search(request, VectorScope.Unscoped, ct);
    }

    public static async Task<VectorQueryResult<string>> Search(
        VectorQueryOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var result = await Search(options.Query, query =>
        {
            query.Top(options.TopK);
            if (options.Filter is not null) query.Where(options.Filter);
            if (!string.IsNullOrWhiteSpace(options.VectorName)) query.Space(options.VectorName);
            if (!string.IsNullOrWhiteSpace(options.SearchText)) query.Text(options.SearchText);
            if (options.Alpha is not null) query.SemanticWeight(options.Alpha.Value);
            if (!string.IsNullOrWhiteSpace(options.ContinuationToken)) query.After(options.ContinuationToken);
        }, ct).ConfigureAwait(false);
        return new VectorQueryResult<string>(result.Items, result.Continuation, VectorTotalKind.Unknown);
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    // Kept out of IntelliSense while untouched adapters are rebuilt; gold-reference code must use Search(...).
    public static Task<VectorQueryResult<string>> SearchLegacy(
        float[] vector,
        string? text = null,
        double? alpha = null,
        int? topK = null,
        object? filter = null,
        string? continuationToken = null,
        string? vectorName = null,
        CancellationToken ct = default) =>
        Search(new VectorQueryOptions(
            vector,
            topK ?? VectorQueryOptions.DefaultTopK,
            continuationToken,
            VectorFilterReader.Read(filter),
            VectorName: vectorName,
            SearchText: text,
            Alpha: alpha), ct);

    public static Task<VectorQueryResult<string>> Search(
        float[] vector,
        VectorRetrieveOptions options,
        CancellationToken ct = default) =>
        Search(new VectorQueryOptions(
            vector,
            options.TopK ?? VectorQueryOptions.DefaultTopK,
            Filter: options.Filter,
            SearchText: options.Text,
            Alpha: options.Alpha), ct);

    public static async Task SaveWithVector(
        TEntity entity,
        ReadOnlyMemory<float> embedding,
        IReadOnlyDictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (EntityContext.Current?.TransactionCoordinator is not null)
            throw new InvalidOperationException(
                "SaveWithVector does not claim cross-store transaction atomicity. Complete the Entity transaction first, then save the vector or use an application compensation workflow.");
        var entitySaved = false;
        try
        {
            await entity.Save(ct).ConfigureAwait(false);
            entitySaved = true;
            await Save(entity.Id, embedding.ToArray(), metadata, ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException && entitySaved)
        {
            throw new VectorCoordinationException(
                $"Vector save failed after Entity '{entity.Id}' committed. Retry the vector save or compensate the Entity write.",
                entity.Id,
                entitySaved: true,
                vectorSaved: false,
                error);
        }
    }

    public static Task SaveWithVector(
        TEntity entity,
        float[] embedding,
        IReadOnlyDictionary<string, object>? metadata = null,
        CancellationToken ct = default) =>
        SaveWithVector(entity, new ReadOnlyMemory<float>(embedding), metadata, ct);

    public static async Task<BatchResult> SaveWithVector(
        IEnumerable<VectorData<TEntity>.VectorEntity> items,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var input = items as IReadOnlyList<VectorData<TEntity>.VectorEntity> ?? items.ToArray();
        foreach (var item in input) await item.Entity.Save(ct).ConfigureAwait(false);
        await Save(input, ct).ConfigureAwait(false);
        return new BatchResult(input.Count, 0, 0);
    }

    private static VectorExecution<TEntity, string> Resolve(DataOperationEffect effect, string operation) =>
        Runtime.Resolve<TEntity, string>(effect, operation);

    private static void ValidateEmbedding(float[] embedding, VectorSpacePlan plan, string parameter)
    {
        if (embedding.Length == 0)
            throw new ArgumentException("Vector embedding cannot be empty.", parameter);
        if (embedding.Length != plan.Dimensions)
            throw new ArgumentException(
                $"Vector embedding has {embedding.Length} dimensions; space '{plan.Name}' requires {plan.Dimensions}.",
                parameter);
        for (var index = 0; index < embedding.Length; index++)
            if (!float.IsFinite(embedding[index]))
                throw new ArgumentException(
                    $"Vector embedding contains a non-finite value at index {index}.",
                    parameter);
    }

    private static void ValidateQuery(
        VectorSearchRequest request,
        IVectorSearchRepository<TEntity, string> repository)
    {
        var capabilities = VectorCaps.Describe(repository, repository.GetType().Name);
        if (request.Filter is not null && !capabilities.Has(VectorCaps.Filters))
            throw VectorFilterUnsupportedException.ForResidual(
                repository.GetType().Name,
                request.Filter);
        if (request.Text is not null && !capabilities.Has(VectorCaps.Hybrid))
            throw new NotSupportedException(
                $"Vector provider '{repository.GetType().Name}' does not support Text/SemanticWeight. Use pure vector search or select a provider that announces Vector Hybrid.");
        if (request.Continuation is not null && !capabilities.Has(VectorCaps.NativeContinuation))
            throw new NotSupportedException(
                $"Vector provider '{repository.GetType().Name}' cannot resume this query snapshot. Remove After(...) or select a provider that announces Vector Continuation.");
    }

    private static Task<TResult> ExecuteInstruction<TResult>(
        string instruction,
        string operation,
        DataOperationEffect effect,
        CancellationToken ct)
    {
        var execution = Resolve(effect, operation);
        return execution.Repository is IInstructionExecutor<TEntity> executor
            ? executor.ExecuteAsync<TResult>(new Instruction(instruction), ct)
            : throw new NotSupportedException(
                $"Vector provider '{execution.Repository.GetType().Name}' does not support the retired instruction '{instruction}'.");
    }
}
