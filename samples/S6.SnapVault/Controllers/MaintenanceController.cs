using Koan.Data.Core;
using Koan.Media.Web.Routing;
using Microsoft.AspNetCore.Mvc;
using S6.SnapVault.Models;
using System.Text.Json;

namespace S6.SnapVault.Controllers;

/// <summary>
/// Maintenance surface (SnapVault step 5d) — trimmed to the two operations that survive the greenfield: storage
/// <b>stats</b> and the destructive <b>wipe</b>. The legacy controller's rebuild-index / clear-cache / optimize-db /
/// export-metadata / backup-config endpoints are dropped: they targeted the deleted architecture (the file-based
/// AI-introspection cache, the eager derivative tiers, a no-op DB "optimize"). Stats + wipe are scoped to the
/// ambient tenant — a studio sees and wipes only its own repository (isolation inherited from the tenant axis, the
/// established SnapVault pattern; no per-endpoint auth).
/// </summary>
[ApiController]
[Route("api/maintenance")]
public sealed class MaintenanceController : ControllerBase
{
    private const double BytesPerGB = 1024.0 * 1024 * 1024;
    private const double BytesPerMB = 1024.0 * 1024;

    private readonly ILogger<MaintenanceController> _logger;

    public MaintenanceController(ILogger<MaintenanceController> logger) => _logger = logger;

    /// <summary>
    /// Storage stats for the settings page. Computed from the entities' own <c>Size</c> (the stored byte count),
    /// tenant-scoped and provider-agnostic — no filesystem walk, no assumption of the Local connector's on-disk
    /// layout. The greenfield collapsed the legacy hot/warm/cold tiers to a single stored-original tier plus the
    /// on-demand render cache, so the response maps honestly: <c>coldTierGB</c> = the stored originals,
    /// <c>warmTierGB</c> = the <see cref="MediaDerivation"/> render cache, <c>hotTierGB</c> = 0. <c>cacheEntries</c>
    /// / <c>cacheSizeMB</c> report that render cache (the closest analog to the legacy AI cache).
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<StorageStats>> GetStats(CancellationToken ct = default)
    {
        var photos = await PhotoAsset.All(ct);
        var derivations = await MediaDerivation.All(ct);

        var originalBytes = photos.Sum(p => p.Size);
        var derivationBytes = derivations.Sum(d => d.Size);

        return Ok(new StorageStats
        {
            HotTierGB = 0,                                                   // greenfield has no separate hot tier
            WarmTierGB = Math.Round(derivationBytes / BytesPerGB, 2),        // the on-demand render cache
            ColdTierGB = Math.Round(originalBytes / BytesPerGB, 2),          // the stored originals
            TotalGB = Math.Round((originalBytes + derivationBytes) / BytesPerGB, 2),
            PhotoCount = photos.Count,
            CacheEntries = derivations.Count,
            CacheSizeMB = (int)Math.Round(derivationBytes / BytesPerMB),
        });
    }

    /// <summary>
    /// Wipe the studio's repository — DESTRUCTIVE. Streams NDJSON progress (<c>{ percentage, message }</c> per line;
    /// the SPA reads <c>percentage</c>/<c>message</c>). Deletes the current tenant's entities in dependency order,
    /// each photo record AND its blob (the deleted derivative types no longer exist, so they're skipped — the
    /// per-photo AfterRemove hook already evicts cached renders + prunes collections). Per-item failures are logged
    /// and skipped, never fatal. Errors are reported as a generic message (never raw exception text on the wire).
    /// </summary>
    [HttpPost("wipe-repository")]
    public async Task WipeRepository(CancellationToken ct = default)
    {
        Response.Headers.Append("Content-Type", "application/x-ndjson");
        Response.Headers.Append("Cache-Control", "no-cache");

        async Task Progress(int percentage, string message)
        {
            await Response.WriteAsync(JsonSerializer.Serialize(new { percentage, message }) + "\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        try
        {
            await Progress(0, "Starting repository wipe…");

            // 1. Collections FIRST (0→5%). Deleting them before the photos means the per-photo AfterRemove hook's
            // collection-prune scan (Collection.All) finds an empty set — no O(photos × collections) save storm.
            var collections = await Collection.All(ct);
            foreach (var collection in collections)
            {
                try { await collection.Remove(ct); } catch (Exception ex) { _logger.LogWarning(ex, "Wipe: failed to delete collection {CollectionId}", collection.Id); }
            }
            await Progress(5, $"Deleted {collections.Count} collection(s)");

            // 2. Photos (5→60%). Delete the blob (best-effort — must not block the record removal) then the record;
            // the record removal fires the AfterRemove hook (cached-render eviction). Wiping the whole tenant's
            // photos, so it is safe to delete blobs even under the fileName-keyed store (no surviving photo can
            // share a to-be-deleted blob — unlike a partial delete).
            var photos = await PhotoAsset.All(ct);
            var totalPhotos = photos.Count;
            var deletedPhotos = 0;
            foreach (var photo in photos)
            {
                try { await photo.Delete(ct); } catch { /* original blob best-effort — the record removal below is what matters */ }
                try
                {
                    await photo.Remove(ct);     // record → AfterRemove cleanup (render eviction + collection pruning)
                    deletedPhotos++;
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Wipe: failed to delete photo {PhotoId}", photo.Id); }

                if (deletedPhotos > 0 && (deletedPhotos % 10 == 0 || deletedPhotos == totalPhotos))
                    await Progress(5 + (int)(deletedPhotos / (double)Math.Max(totalPhotos, 1) * 55),
                        $"Deleting photos… {deletedPhotos}/{totalPhotos}");
            }
            await Progress(60, $"Deleted {deletedPhotos} photo(s)");

            // 3. Events (60→70%).
            var events = await Event.All(ct);
            foreach (var evt in events)
            {
                try { await evt.Remove(ct); } catch (Exception ex) { _logger.LogWarning(ex, "Wipe: failed to delete event {EventId}", evt.Id); }
            }
            await Progress(70, $"Deleted {events.Count} event(s)");

            // 4. Background job records (70→85%).
            var jobs = await PhotoProcessingJob.All(ct);
            foreach (var job in jobs)
            {
                try { await job.Remove(ct); } catch (Exception ex) { _logger.LogWarning(ex, "Wipe: failed to delete job {JobId}", job.Id); }
            }
            await Progress(85, $"Deleted {jobs.Count} job(s)");

            // 5. Session + render-cache mop-up (85→100%): PhotoSetSession snapshots and any MediaDerivation rows the
            // per-photo eviction didn't reach (a source-less render, or one whose photo delete failed above).
            // NOTE (5e): the studio's [HostScoped] guest-lifecycle rows (GalleryGrant / GalleryInvite /
            // ProofSelection, keyed by StudioTenantId) are NOT wiped here — the guest surface doesn't exist until
            // 5e, and clearing them needs the operator's resolved tenant id that 5e's subject middleware provides.
            // They orphan inert until then (their events/photos are gone, so SEC-0008 already yields nothing).
            var sessions = await PhotoSetSession.All(ct);
            foreach (var session in sessions)
            {
                try { await session.Remove(ct); } catch (Exception ex) { _logger.LogWarning(ex, "Wipe: failed to delete session {SessionId}", session.Id); }
            }
            var leftoverDerivations = await MediaDerivation.All(ct);
            foreach (var derivation in leftoverDerivations)
            {
                try { await derivation.Delete(ct); } catch { /* blob best-effort */ }
                try { await derivation.Remove(ct); } catch (Exception ex) { _logger.LogWarning(ex, "Wipe: failed to delete derivation {DerivationId}", derivation.Id); }
            }

            await Progress(100, "Repository wiped successfully");
            _logger.LogWarning("Repository wiped: {Photos} photos, {Events} events, {Collections} collections, {Jobs} jobs",
                deletedPhotos, events.Count, collections.Count, jobs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Repository wipe failed");
            await Progress(-1, "Error: the wipe did not complete. See the server logs.");   // sanitized — no raw exception text
        }
    }
}

/// <summary>Storage stats shape the settings page reads (fields are camelCased on the wire).</summary>
public sealed class StorageStats
{
    public double HotTierGB { get; set; }
    public double WarmTierGB { get; set; }
    public double ColdTierGB { get; set; }
    public double TotalGB { get; set; }
    public int PhotoCount { get; set; }
    public int CacheEntries { get; set; }
    public int CacheSizeMB { get; set; }
}
