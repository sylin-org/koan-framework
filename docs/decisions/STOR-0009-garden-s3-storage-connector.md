---

## Current implementation boundary

As of 2026-07-16 this ADR is only partially realized. The S3 provider and its S3 operations, bucket
naming, explicit endpoint configuration, and lazy two-hop garden storage lookup ship. S3 does **not**
accept `zen-garden://` connection intents, does not participate in offering discovery, and does not use
an offering-binding contract. `GardenAwareEndpointManager<TConnection>` exists in `Koan.ZenGarden`, but
the S3 provider is not currently wired through it; the resilient decorator described below is not a
supported product surface. Treat those portions as target design, not current instructions.
id: STOR-0009
slug: STOR-0009-garden-s3-storage-connector
domain: STOR
title: Garden-aware S3 storage connector with resilient decorator
status: Accepted
date: 2026-03-18
---

Context

- The only existing IStorageProvider implementation is LocalStorageProvider (STOR-0005).
- Applications need to store files on garden-managed storage (physical drives, NAS) transparently.
- Moss exposes S3-compatible endpoints per storage replica set on dedicated ports (Zen Garden STORAGE-0016). S3 objects and native files share a unified namespace — S3 is just another protocol for the same storage.
- Storage devices can be physically removed and re-plugged at any time, possibly on a different stone.
- The Koan.ZenGarden bridge already provides offering discovery, SSE event subscriptions, and capability resolution.
- S6.SnapVault demonstrates production storage patterns (three-tier profiles, StorageEntity, derivative media) and is the target integration sample.
- Zen Garden names the default replica set "storage" (constant DEFAULT_REPLICA_SET_DISPLAY). Named sets use FQN format: "storage", "storage::images", "storage::archive".
- The garden-aware adapters (MongoDB, Ollama) each independently implement circuit breaker, SSE subscription, and endpoint hot-swap logic. This duplication should be extracted into a shared abstraction.

Decision

1. New project: Koan.Storage.Connector.S3

- Assembly: Koan.Storage.Connector.S3
- NuGet dependency: Minio (MinIO .NET SDK)
- Provider name: "s3"
- Follows Reference = Intent: adding the project reference auto-registers the provider via KoanAutoRegistrar.

2. S3StorageProvider implements the full provider contract

- IStorageProvider (Name, Capabilities, WriteAsync, OpenReadAsync, OpenReadRangeAsync, DeleteAsync, ExistsAsync)
- IStatOperations (HeadAsync)
- IPresignOperations (PresignReadAsync, PresignWriteAsync) — calls Moss-native presign endpoint (STORAGE-0016 §2f)
- IServerSideCopy (CopyAsync) — Moss STORAGE-0016 implements CopyObject via x-amz-copy-source header
- IListOperations (ListObjectsAsync) — uses ListObjectsV2 (Moss STORAGE-0016 implements both V1 and V2)
- StorageProviderCapabilities: SupportsSequentialRead=true, SupportsSeek=true, SupportsPresignedRead=true, SupportsServerSideCopy=true
- OpenReadRangeAsync uses HTTP Range header on GetObject; Moss returns 206 with Content-Range (see STORAGE-0016 §2a).
- CopyAsync uses S3 CopyObject (PUT with x-amz-copy-source); enables efficient cross-profile transfer within the same storage without streaming (see STORAGE-0016 §2b).

3. Bucket naming uses ApplicationIdentity.Code

- Bucket format: {AppIdentity.Code}-{container} (accessed at runtime via AppHost.Identity.Code)
- Example: S6.SnapVault with container "photos" → bucket "snap-vault-photos"
- Overridable via S3StorageOptions.BucketPrefix for apps that need custom naming.
- Buckets are auto-created on first write. Explicit CreateBucket (PUT /{bucket}) also available (STORAGE-0016 §2). S3StorageProvider calls EnsureBucketExists on first write per container; no separate CreateBucketAsync on IStorageProvider since bucket lifecycle is a transport concern, not a storage contract concern.
- Since S3 buckets are directories at the storage mount root (STORAGE-0016 §1), the AppIdentity.Code prefix prevents collisions between apps and between app-managed and user-managed directories.

4. Two-step discovery via ZenGarden

Step 1 — Garden-level (which stone has the storage?):
- Call ZenGarden.Storage.Catalog(replicaSetName) to get the seed-bank tool snapshot.
- Snapshot contains Connection with protocol, hostname, IP, port, URIs.
- The port and URIs reflect the S3 listener port (STORAGE-0016), not the Moss HTTP port.
- The default replica set name is "storage" (Zen Garden constant DEFAULT_REPLICA_SET_DISPLAY).

Step 2 — Connection:
- MinIO SDK connects to the resolved endpoint (e.g., http://stone-01.local:23400).
- Standard S3 at root /. No path prefix, no custom headers.

Fallback — Explicit configuration:
- If ZenGarden is not available or not referenced, S3StorageOptions.Endpoint is used directly.
- Supports manual configuration for non-garden deployments (direct MinIO, AWS S3, etc.).

Note: the port catalog endpoint (GET /api/v1/stone/storage/s3/ports, STORAGE-0016 §4) is diagnostic-only. Koan consumers use SSE-based discovery exclusively — the tool snapshot already contains connection.port and connection.uris.

5. Current S3 endpoint resolution

- `S3StorageOptionsConfigurator` binds explicit S3 settings only.
- `S3StorageProvider` resolves lazily at first use because application identity is not ready during options binding.
- Explicit `Koan:Storage:Providers:S3:Endpoint` wins.
- Without an explicit endpoint, the provider uses the active Zen Garden client's bound Moss endpoint and the garden storage APIs to locate the primary storage stone and its S3 block.
- Failure is corrective; no `zen-garden://` URI or offering fallback is advertised.

6. Storage is not an offering binding

- Storage remains a first-class garden concept and uses the storage catalog/API.
- Adapter service-name/alias contribution applies to service discovery; it is not reused for seed-bank or storage topology merely for symmetry.
- A future Storage contribution target must be earned from matching Storage semantics and lifecycle.

7. GardenAwareEndpointManager in Koan.ZenGarden

Extract the circuit breaker, SSE subscription, and endpoint hot-swap pattern into a shared component. The current MongoDB, Ollama, and now S3 adapters each independently implement the same concerns: subscribe to offering availability, detect failure, swap endpoint, recover. This duplication is eliminated by a shared abstraction.

GardenAwareEndpointManager<TConnection>:
- Subscribes to ZenGarden SSE for a specific tool/offering.
- Manages circuit state: Closed → Open (on failure) → HalfOpen (on SSE ready event) → Closed (on successful probe).
- Accepts synchronous failure signals via ReportFailure() — adapters call this when they receive transport errors (e.g., S3StorageProvider calls it on 503 from MinIO SDK). This opens the circuit immediately without waiting for SSE propagation.
- Calls a TConnectionFactory delegate when the endpoint changes, returning a new TConnection.
- Exposes: CurrentEndpoint, IsAvailable, OnEndpointChanged event, ReportFailure().
- Each adapter provides: CreateConnection(endpoint), DisposeConnection(old), HealthCheck(connection).

Adapter integration:
- S3StorageProvider: TConnection = IMinioClient. Factory creates new MinioClient with new endpoint. WAL replay triggers on circuit close.
- MongoDB (future): TConnection = IMongoClient. Factory creates new MongoClient with new connection string.
- Ollama (future): TConnection = HttpClient. Factory creates new HttpClient with new base URL.

The WAL (write-ahead log) remains in Koan.Storage — write-behind caching is storage-specific. The manager handles the universal concern: "my remote endpoint moved or failed."

8. ResilientStorageDecorator in Koan.Storage core

A provider-agnostic decorator that wraps any IStorageProvider with write-behind cache, using GardenAwareEndpointManager (§7) for circuit breaker and event-driven recovery.

Circuit states (managed by GardenAwareEndpointManager):
- Closed — primary provider is healthy; all operations go to primary.
- Open — primary is unavailable; writes go to local WAL; reads attempt local cache, then fail.
- HalfOpen — primary responded to a probe; next write attempts primary.

Write-Ahead Log (WAL):
- Location: .Koan/storage-wal/{replicaSet}-{provider}-{container}/
- Journal: journal.jsonl — ordered operation log (put, delete).
- Staging: staging/ — file content for pending writes, keyed by content hash.
- On recovery: replay journal in order against primary. Last-writer-wins conflict resolution.
- Size cap: configurable maximum WAL size (default 500 MB); oldest entries evicted when exceeded.

Dual detection mechanism:

Mechanism 1 — Synchronous (503 from S3 port):
- S3 request returns 503 Service Unavailable.
- Circuit breaker opens immediately via GardenAwareEndpointManager.
- Current write redirects to WAL.
- No dependency on event propagation latency.

Mechanism 2 — Event-driven (ZenGarden SSE):
- GardenAwareEndpointManager subscribes to ZenGarden.Storage.On(replicaSetName).
- On Offline/Unavailable: confirms circuit is open (redundant with 503, but authoritative).
- On Online/Ready: triggers recovery flow.

Recovery flow (event-driven, no polling):
- ZenGarden SSE delivers tool.upsert with state=Ready for the storage.
- Snapshot contains updated Connection (possibly new stone, new port).
- GardenAwareEndpointManager calls CreateConnection with new endpoint.
- S3StorageProvider receives new MinIO client via OnEndpointChanged.
- Circuit breaker transitions to HalfOpen, then Closed on first successful write.
- WAL replays journal entries against the new endpoint.
- On successful flush: WAL journal and staging files are deleted.

9. Configuration surface

Per-profile resilience opt-in via StorageOptions:

```json
{
  "Koan": {
    "Storage": {
      "Providers": {
        "S3": {
          "Endpoint": null,
          "AccessKey": "minioadmin",
          "SecretKey": "minioadmin",
          "BucketPrefix": null,
          "ReplicaSet": null,
          "PathStyle": true
        }
      },
      "Profiles": {
        "cold": {
          "Provider": "s3",
          "Container": "photos",
          "Resilient": true
        }
      }
    }
  }
}
```

- Endpoint: null triggers ZenGarden discovery; explicit value bypasses it.
- AccessKey/SecretKey: default to MinIO defaults; unsigned/public for phase 1.
- BucketPrefix: overrides AppHost.Identity.Code if set.
- ReplicaSet: seed-bank replica set name (null = "storage", the Zen Garden default).
- Resilient: enables ResilientStorageDecorator for this profile.

10. CDC: Storage content change subscriptions

Moss emits StorageTick events via SSE for every changelog entry (STORAGE-0016 §8).
Since S3 writes now share the unified namespace, every PutObject and DeleteObject
generates a tick. Koan exposes this as a typed subscription surface.

New API on ZenGardenStorageSurface:

```csharp
ZenGarden.Storage.OnContentChange("storage", async (changes, ct) =>
{
    foreach (var change in changes.Where(c => c.Path.StartsWith("snap-vault-")))
    {
        if (change.Op == ChangeOp.Create)
            await ProcessNewFile(change.Path, ct);
    }
});
```

Under the hood:
- Subscribe to StorageTick SSE at GET /api/v1/stone/storage/stream (doorbell: "something changed").
- On tick, pull GET /api/v1/stone/storage/banks/{name}/changes?since={cursor}.
- Parse changelog entries into typed StorageContentChange records.
- Filter by bucket prefix matching AppHost.Identity.Code to scope to this app's data.
- Dispatch to registered handlers.

Use cases:
- Auto-generate thumbnails when a photo is uploaded (via S3, WebDAV, or Explorer).
- Auto-create embeddings when a document is stored.
- Maintain a search index that stays consistent with storage.
- React to changes made by other apps or by users via the cloud drive.

The handler receives changes from ALL sources (S3, REST, WebDAV, cloud drive, replication) because they all share the same namespace and the same changelog.

11. S6.SnapVault integration

- Add project reference: Koan.Storage.Connector.S3.
- Update appsettings.json: Provider "local" → "s3" in all three profiles.
- No code changes required (Reference = Intent, auto-registrar handles DI).
- S6.SnapVault already references Koan.ZenGarden, so two-step discovery works automatically.

Scope

- In scope: S3 provider (including presigned URLs via Moss-native token scheme and multipart upload support via MinIO SDK), ZenGarden discovery, GardenAwareEndpointManager, resilient decorator with WAL, CDC subscription surface, ForStorage fqid bug fix, S6.SnapVault integration.
- Out of scope: AWS Signature V4 authentication (not needed on private network; Moss-native presign provides equivalent security), local-first write mode (high risk, niche), multi-instance WAL merge conflict resolution.

Implementation notes

- S3StorageProvider is stateless per request; MinIO SDK client is managed by GardenAwareEndpointManager.
- On endpoint change, the manager calls CreateConnection which returns a new IMinioClient. The old client is disposed.
- WAL journal entries are append-only, flushed to disk on each write (fsync for durability).
- ResilientStorageDecorator is registered as a wrapper via StorageService, not as a separate IStorageProvider. StorageService detects Resilient=true on the profile and wraps the resolved provider.
- GardenAwareEndpointManager subscribes to ZenGarden events only when Koan.ZenGarden is referenced (optional dependency). Without ZenGarden, recovery falls back to timeout-based retry.
- CDC subscription (§10) is opt-in. Apps that do not call OnContentChange incur no overhead (no SSE subscription, no change polling).

Consequences

- Positive: applications use garden-managed storage transparently via standard StorageEntity patterns.
- Positive: storage is location-transparent — device can move between stones; app follows via events.
- Positive: S3 writes are automatically replicated to Dormant replicas (unified namespace, STORAGE-0016 §1).
- Positive: CDC enables reactive processing pipelines triggered by any source (S3, WebDAV, Explorer).
- Positive: GardenAwareEndpointManager eliminates duplicated circuit breaker / SSE logic across adapters.
- Positive: write continuity during storage removal — WAL prevents data loss.
- Positive: no custom HTTP clients — MinIO SDK provides battle-tested S3 implementation.
- Negative: WAL introduces local disk usage during disconnected periods.
- Negative: read availability during disconnect is limited to locally cached files (cold reads fail).
- Risk: WAL size can grow unbounded during extended disconnects; mitigated by configurable cap with oldest-eviction.
- Risk: single-app assumption for WAL — multiple app instances writing to the same WAL require coordination not covered in this phase.

References

- STOR-0001 module and contracts
- STOR-0005 LocalStorageProvider baseline design
- STOR-0006 storage default routing and fallbacks
- STOR-0008 storage DX stream and key-first helpers
- Zen Garden: STORAGE-0016 Unified S3 storage gateway (companion ADR)
- Zen Garden: STORAGE-0006 seed-bank replication
- Zen Garden: STORAGE-0009 managed storage and file sharing
- Zen Garden: STORAGE-0013 replica set identity model
- Zen Garden: STORAGE-0015 StorageRouter and domain policy extraction
