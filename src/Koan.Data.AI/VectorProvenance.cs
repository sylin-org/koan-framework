using Koan.Data.Vector;

namespace Koan.Data.AI;

/// <summary>
/// Builds the reserved <c>__embedding.*</c> provenance metadata (AI-0036) that the embedding
/// lifecycle owner stamps onto a stored vector, recording which model/source produced it.
/// </summary>
/// <remarks>
/// Before AI-0036 every <c>SaveWithVector</c> call site passed <c>null</c> metadata, so the
/// producing model — known to the worker, the sync hook, and the migrator — was dropped at the
/// store boundary. A subsequent model change then silently created a mixed-space index (vectors
/// from different models are not comparable). This builder is the single choke point all three
/// write paths route through. The store persists the result as ordinary filterable metadata and
/// never interprets it (the keys live in <see cref="VectorProvenanceKeys"/>, store-side).
/// </remarks>
public static class VectorProvenance
{
    /// <summary>
    /// Builds the provenance dictionary, optionally layered over caller-supplied metadata.
    /// Reserved <c>__embedding.*</c> keys are authoritative and overwrite any colliding caller key.
    /// Returns <c>null</c> only when there is genuinely nothing to record and nothing to merge,
    /// preserving the prior <c>null</c>-metadata contract for undecorated/convention paths.
    /// </summary>
    /// <param name="model">The producing model (e.g. the resolved <c>[Embedding(Model=…)]</c> or migration target).</param>
    /// <param name="source">The configured source/route (e.g. <c>"openai-prod"</c>).</param>
    /// <param name="version">The <c>[Embedding]</c> schema version; <c>0</c> means "unset", omitted from the result.</param>
    /// <param name="provider">The provider; when null it is derived from <paramref name="source"/>.</param>
    /// <param name="merge">Caller metadata to carry through alongside the provenance keys.</param>
    public static IReadOnlyDictionary<string, object>? Build(
        string? model,
        string? source,
        int version,
        string? provider = null,
        IReadOnlyDictionary<string, object>? merge = null)
    {
        provider ??= DeriveProvider(source);

        var hasProvenance = model is not null || source is not null || provider is not null || version != 0;
        if (!hasProvenance && merge is null)
            return null;

        var dict = merge is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(merge);

        if (model is not null) dict[VectorProvenanceKeys.Model] = model;
        if (source is not null) dict[VectorProvenanceKeys.Source] = source;
        if (provider is not null) dict[VectorProvenanceKeys.Provider] = provider;
        if (version != 0) dict[VectorProvenanceKeys.Version] = version;

        return dict.Count == 0 ? null : dict;
    }

    /// <summary>
    /// Derives the provider from a source like <c>"openai-prod"</c> → <c>"openai"</c>.
    /// Centralizes the convention previously inlined across the worker/migrator/telemetry.
    /// </summary>
    public static string? DeriveProvider(string? source)
        => source?.Split('-').FirstOrDefault();
}
