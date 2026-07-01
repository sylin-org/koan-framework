using Koan.Data.Core;
using Koan.Media.Web.Routing;
using Microsoft.Extensions.Logging;
using S6.SnapVault.Models;

namespace S6.SnapVault.Initialization;

/// <summary>
/// Structural delete-cleanup for <see cref="PhotoAsset"/> (SnapVault §9.7 tripwire). Registered ONCE at boot as a
/// data-layer <c>AfterRemove</c> lifecycle hook, so it fires on EVERY delete path — bulk delete today, client
/// deprovisioning tomorrow — instead of being remembered per-controller (conformity-by-design). Two best-effort
/// cleanups, both "storage-cleanliness, never a serving leak": the SEC-0008 access gate precedes the derivation
/// cache (<see cref="MediaEntitySource{TEntity}"/> resolves the source before any cached render), so an
/// un-evicted render is already unreachable.
/// <list type="number">
/// <item>Reclaim this photo's own stored original blob. Only safe because ingest keys each blob by a fresh
/// <c>StringId</c> (not the fileName), so no sibling can share the key — the fix that turned a per-controller
/// "whole-tenant wipe only" caveat into a structural, every-path reclaim (§9.7).</item>
/// <item>Evict this source's cached recipe renders (<see cref="MediaDerivation"/>) — record <b>and</b> blob.
/// Targeted by <c>SourceMediaId</c>, not the framework's probe-if-orphaned sweep (which the default
/// <c>MediaEntitySource</c> leaves a no-op); targeting also cannot false-positive a still-live but access-gated
/// source into deletion.</item>
/// <item>Drop the dead id from any collection's ordered <c>PhotoIds</c> membership (no framework equivalent).</item>
/// </list>
/// Each leg is wrapped so a cleanup failure never fails the user's delete — the photo row is already gone.
///
/// <para>INVARIANT: the hook runs in the ambient tenant of the triggering delete, so the (tenant-scoped, not
/// access-scoped) <c>Collection.All</c> / <c>MediaDerivation.Query</c> only ever touch that tenant's rows. Every
/// SnapVault delete path establishes a tenant (the operator's request, or a deprovisioning job's rehydrated
/// tenant) — a future caller that removes a photo OUTSIDE a tenant scope would fan the collection scan across all
/// tenants, so keep <c>PhotoAsset.Remove</c> inside an established tenant ambient.</para>
/// </summary>
internal static class PhotoAssetCleanup
{
    public static void Register(ILogger logger)
    {
        PhotoAsset.Events.AfterRemove(async ctx =>
        {
            var photoId = ctx.Current.Id;
            var ct = ctx.CancellationToken;

            // 0. Reclaim this photo's own stored original blob. Safe now that ingest keys each photo's blob uniquely
            // (a fresh StringId, not the fileName) — no sibling can share the key, so this runs on EVERY delete path
            // (bulk delete, client deprovisioning) instead of leaking the original bytes (§9.7 tripwire). Best-effort:
            // the record is already gone, and a missing/absent blob must never fail the user's delete.
            if (!string.IsNullOrEmpty(ctx.Current.Key))
            {
                try { await ctx.Current.Delete(ct); }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to delete original blob for removed photo {PhotoId}", photoId); }
            }

            // 1. Evict cached recipe renders whose source is this photo (blob first, then the record). Guard EACH
            // derivation independently so one failed eviction can't strand the rest (the outer guard is only for the
            // Query itself, which is genuinely whole-leg).
            try
            {
                var derivations = await MediaDerivation.Query(d => d.SourceMediaId == photoId, ct);
                foreach (var derivation in derivations)
                {
                    try
                    {
                        try { await derivation.Delete(ct); } catch { /* blob may already be gone — the record removal is what matters */ }
                        await derivation.Remove(ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to evict cached render {DerivationId} for deleted photo {PhotoId}", derivation.Id, photoId);
                    }
                }
                if (derivations.Count > 0)
                    logger.LogInformation("Evicted cached render(s) for deleted photo {PhotoId} ({Count} candidate(s))", photoId, derivations.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to enumerate cached renders for deleted photo {PhotoId}", photoId);
            }

            // 2. Prune the dead id from any collection's manual membership so counts stay honest. Guard EACH
            // collection Save independently — a failure on one must not leave the id stranded in the others.
            try
            {
                var collections = await Collection.All(ct);
                foreach (var collection in collections)
                {
                    if (!collection.PhotoIds.Remove(photoId)) continue;
                    collection.UpdatedAt = DateTime.UtcNow;
                    try
                    {
                        await collection.Save(ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to prune collection {CollectionId} after deleting photo {PhotoId}", collection.Id, photoId);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to enumerate collections after deleting photo {PhotoId}", photoId);
            }
        });
    }
}
