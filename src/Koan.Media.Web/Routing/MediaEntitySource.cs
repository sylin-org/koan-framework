using System.Security.Cryptography;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Media;
using Koan.Media.Abstractions.Model;
using Koan.Media.Abstractions.Recipes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Media.Web.Routing;

/// <summary>
/// The default <see cref="IMediaSource"/> for a <see cref="MediaEntity{TEntity}"/>-derived
/// content type. This is the one meaningful part an app needs to serve its media recipes: it
/// resolves a media id <b>through the entity layer</b> (<c>Data&lt;TEntity, string&gt;.Get</c>),
/// not through raw storage — so <b>every active read predicate applies to media serving structurally,
/// not per-endpoint</b>:
/// <list type="bullet">
///   <item>tenant isolation (the request's ambient tenant scopes the lookup), and</item>
///   <item>request context contributed through <c>IWebContextContributor</c> (for example, a validated
///   share link can contribute the gallery predicate once for every downstream entity read).</item>
/// </list>
/// A request whose contributed context/tenant can't see the entity gets a <c>null</c> handle, and the
/// <see cref="Controllers.MediaController"/> returns 404. Because the controller calls
/// <see cref="OpenAsync"/> <i>before</i> any derivation-cache lookup, even a cached recipe render
/// is gated by this resolve — the access check can't be bypassed by a warm cache.
///
/// <para>The bytes stream from the single stored original; recipes transform it on demand.
/// Register it for an app's media type with one line —
/// <c>services.AddSingleton&lt;IMediaSource, MediaEntitySource&lt;PhotoAsset&gt;&gt;()</c> — or
/// subclass it (<c>sealed class PhotoMedia : MediaEntitySource&lt;PhotoAsset&gt;;</c>). Apps with
/// several media types register one per type behind their own routing; the framework does not
/// guess which type a bare <c>/media/{id}</c> id belongs to.</para>
/// </summary>
/// <typeparam name="TEntity">The app's <see cref="MediaEntity{TEntity}"/>-derived content type.</typeparam>
public class MediaEntitySource<TEntity> : IMediaSource
    where TEntity : MediaEntity<TEntity>
{
    /// <inheritdoc />
    public async Task<MediaSourceHandle?> OpenAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        // Entity-layer resolve. RepositoryFacade folds in every active read predicate (tenant +
        // Web-contributed context): a gated-out request yields null here → 404 upstream.
        var entity = await Entity<TEntity, string>.Get(id, ct).ConfigureAwait(false);
        if (entity is null) return null;

        // Instance OpenRead is tenant-scoped (STOR-0011 StorageScope) and streams the stored
        // original — no full-buffer allocation on the raw-original fast path.
        var bytes = await entity.OpenRead(ct).ConfigureAwait(false);

        // ContentHashHex is the source-side half of the recipe cache key and the ETag. Prefer the
        // stored content hash; fall back to the (immutable) storage key, then the id.
        var hash = !string.IsNullOrEmpty(entity.ContentHash) ? entity.ContentHash!
                 : !string.IsNullOrEmpty(entity.Key) ? entity.Key!
                 : id;

        return new MediaSourceHandle(
            Id: id,
            Bytes: bytes,
            ContentHashHex: hash,
            // Emit Last-Modified (MEDIA-0003 canon) from the entity's own timestamps. Safe as a validator:
            // the stored bytes are immutable per key, so the timestamp only ever moves forward on a re-store.
            LastModified: entity.UpdatedAt ?? entity.CreatedAt,
            ContentType: string.IsNullOrEmpty(entity.ContentType) ? "application/octet-stream" : entity.ContentType!);
    }

    // ----- Derivation cache (MEDIA-0007): persist recipe renders so repeat requests skip the pipeline. -----
    // The controller calls OpenAsync (the access gate) BEFORE OpenDerivationAsync, so a cached render is only
    // ever served to a caller who already resolved the source — the cache cannot bypass access scoping.

    /// <inheritdoc />
    public async Task<MediaDerivationHandle?> OpenDerivationAsync(
        string sourceId, string recipeFingerprint, CancellationToken ct = default)
    {
        var row = await MediaDerivation
            .Get(MediaDerivation.KeyFor(sourceId, recipeFingerprint), ct).ConfigureAwait(false);
        if (row is null) return null;
        var bytes = await row.OpenRead(ct).ConfigureAwait(false);
        return new MediaDerivationHandle(
            bytes, string.IsNullOrEmpty(row.ContentType) ? "application/octet-stream" : row.ContentType!);
    }

    /// <inheritdoc />
    public async Task TryStoreDerivationAsync(
        string sourceId, string recipeFingerprint, MediaOutput output,
        string? recipeName, string? recipeVersion, CancellationToken ct = default)
    {
        var rowId = MediaDerivation.KeyFor(sourceId, recipeFingerprint);
        // Idempotent: the fingerprint pins an immutable render, so a present row is already correct.
        if (await MediaDerivation.Get(rowId, ct).ConfigureAwait(false) is not null) return;

        // Materialize the render bytes via the streaming terminal (not the obsolete MediaOutput.Bytes).
        using var buffer = new MemoryStream();
        await output.WriteToAsync(buffer, ct).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        // Content-addressed blob key: filesystem-safe hex + dedups byte-identical renders.
        var blobKey = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var row = await MediaDerivation.Onboard(blobKey, new MemoryStream(bytes), output.ContentType, ct)
            .ConfigureAwait(false);
        row.Id = rowId;
        row.SourceMediaId = sourceId;
        row.DerivationKey = recipeFingerprint;
        row.RecipeName = recipeName;
        row.RecipeVersion = recipeVersion;
        await row.Save(ct).ConfigureAwait(false);
    }
}

/// <summary>Registration verb for the media serving source.</summary>
public static class MediaSourceServiceCollectionExtensions
{
    /// <summary>
    /// Register the app's <see cref="MediaEntity{TEntity}"/>-derived content type as the source the recipe
    /// controller serves — the one line that makes <c>GET /media/{id}[/{recipe}]</c> resolve through current Entity read context,
    /// from that type's stored originals. Named + generic-constrained so the intent is greppable and the
    /// wrong type can't be registered. The framework does not auto-pick a type (a bare <c>/media/{id}</c>
    /// carries no discriminator); an app with several media types registers one per type behind its own routing.
    /// </summary>
    public static IServiceCollection AddMediaSource<TEntity>(this IServiceCollection services)
        where TEntity : MediaEntity<TEntity>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<IMediaSource, MediaEntitySource<TEntity>>());
        return services;
    }
}
