using System.Diagnostics;
using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector;

namespace Koan.Data.AI;

/// <summary>
/// Owns the one lifecycle-to-vector write boundary for Entity embeddings.
/// </summary>
/// <remarks>
/// Callers retain their own intent—ordinary lifecycle, deferred work, or explicit migration—but
/// none of them may re-save the domain Entity or reproduce model routing, provenance, guard, and
/// state persistence independently.
/// </remarks>
internal static class EmbeddingWriter
{
    internal static EmbeddingContent Describe<TEntity>(TEntity entity, EmbeddingMetadata metadata)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(metadata);

        var text = metadata.BuildEmbeddingText(entity);
        var signature = EmbeddingMetadata.ComputeSignature($"v{metadata.Version}:{text}");
        return new EmbeddingContent(text, signature);
    }

    internal static async ValueTask<EmbeddingWrite> Write<TEntity>(
        TEntity entity,
        EmbeddingMetadata metadata,
        EmbeddingContent content,
        string? targetModel = null,
        string? targetSource = null,
        string? targetProvider = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(metadata);

        var model = targetModel ?? metadata.Model;
        var source = targetSource ?? metadata.Source;
        var providerCall = Stopwatch.StartNew();

        float[] embedding;
        using (source is not null ? Client.Scope(embed: source) : null)
        {
            embedding = model is null
                ? await Client.Embed(content.Text, ct).ConfigureAwait(false)
                : await Client.Embed(content.Text, new EmbedOptions { Model = model }, ct).ConfigureAwait(false);
        }

        providerCall.Stop();

        await VectorModelGuard.GuardWrite<TEntity>(model, ct: ct).ConfigureAwait(false);

        var provenance = VectorProvenance.Build(
            model,
            source,
            metadata.Version,
            targetProvider,
            VectorFilterableMetadata.Extract(entity));

        // The Entity was persisted by the lifecycle caller, loaded by the deferred worker, or supplied
        // deliberately by a migration. Re-saving it here would cross the Data/AI responsibility seam and
        // recursively invoke lifecycle behavior. This boundary is therefore vector-only by construction.
        await VectorData<TEntity>.Save(entity, embedding, provenance, ct).ConfigureAwait(false);

        var stateId = EmbeddingState<TEntity>.MakeId(entity.Id);
        var state = await EmbeddingState<TEntity>.Get(stateId, ct).ConfigureAwait(false)
            ?? new EmbeddingState<TEntity>
            {
                Id = stateId,
                EntityId = entity.Id,
                ContentSignature = content.Signature
            };

        state.ContentSignature = content.Signature;
        state.LastEmbeddedAt = DateTimeOffset.UtcNow;
        state.Model = model;
        await state.Save(ct).ConfigureAwait(false);

        return new EmbeddingWrite(model, source, providerCall.Elapsed);
    }
}

internal readonly record struct EmbeddingContent(string Text, string Signature);

internal readonly record struct EmbeddingWrite(
    string? Model,
    string? Source,
    TimeSpan ProviderLatency);
