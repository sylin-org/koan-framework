---
id: STOR-0010
slug: STOR-0010-replicated-storage-with-local-cache
domain: STOR
title: Replicated storage with local cache tier
status: Proposed
date: 2026-03-19
evolves: STOR-0005, STOR-0006, STOR-0009
---

## Context

- Koan.Storage provides `IStorageProvider` with local and S3 implementations (STOR-0005, STOR-0009).
- Applications configure profiles (`cold: { Container: "photos" }`) routed to specific providers.
- Current model: one provider per profile. Switching from local to S3 loses access to locally stored data. S3 unavailability means total failure.
- Container lifecycle (destroy/recreate) loses local data. Users expect the app to recover transparently when remote storage has the data.
- The framework principle "Reference = Intent" should extend to storage topology: referencing an S3 connector should upgrade storage behavior automatically, not require explicit mode configuration.
- Storage operations must never lose data. Eviction of local files is only safe when a durable remote copy exists.

## Decision

### 1. Storage modes

Each profile operates in one of four modes:

| Mode | Write | Read | Delete | Local data |
|---|---|---|---|---|
| `local` | Local only | Local only | Local only | Standalone store |
| `remote` | Remote only | Remote only | Remote only | Nothing on disk |
| `replicated` | Local first, async push | Local first, pull-through on miss | Both, orchestrated | Bounded cache |
| (absent) | Auto-detected | Auto-detected | Auto-detected | Auto |

**Auto-detection** (absent mode): the framework inspects registered providers and connectivity. One provider → use it. Local + remote both registered and remote reachable → `replicated`. Local + remote but remote unreachable → `local` with upgrade to `replicated` when remote becomes available.

Profile configuration:

```json
{
  "Koan": {
    "Storage": {
      "Profiles": {
        "photos": { "Container": "photos" },
        "sensitive": { "Container": "vault", "Mode": "remote" },
        "scratch": { "Container": "tmp", "Mode": "local" }
      }
    }
  }
}
```

Absent `Mode` = auto. Absent `Provider` = auto (framework selects based on registered providers). The developer only specifies the container name in the common case.

### 2. ReplicatedStorageProvider

A core `Koan.Storage` component that composes two `IStorageProvider` instances:

```
ReplicatedStorageProvider
  ├── cache: IStorageProvider     (fast, bounded, evictable — typically local)
  └── durable: IStorageProvider   (source of truth, unbounded — typically remote)
```

Both `cache` and `durable` are opaque `IStorageProvider` implementations. `ReplicatedStorageProvider` has no knowledge of S3, filesystem, or any specific backend. It orchestrates:

**Write flow**:
1. Write to `cache` synchronously (fast, app returns immediately).
2. Append entry to sync journal: `{ op: "put", container, key, contentHash, timestamp }`.
3. Background sync task drains the journal → pushes to `durable`.
4. On successful push, mark journal entry as synced.

**Read flow**:
1. Try `cache.OpenReadAsync()`. If found, return.
2. Cache miss: call `durable.OpenReadAsync()`.
3. If found: write to `cache` (pull-through), update manifest, return stream.
4. If both miss: throw `FileNotFoundException`.

**Read (streaming pull-through for large files)**:
For files exceeding a size threshold (default 10MB), return a stream that reads from `durable` while simultaneously writing to `cache`. The app begins reading before the full file is cached. Below threshold, buffer-then-return for simplicity.

**Delete flow**:
1. Delete from `cache`.
2. Append to sync journal: `{ op: "delete", container, key, timestamp }`.
3. Background sync task drains → deletes from `durable`.
4. If `durable` unreachable, queue for retry.

### 3. Local manifest

A lightweight index of all known objects across both tiers. Stored at `.Koan/storage-manifest/{container}.jsonl` (append-only, periodic compaction).

Each entry:

```json
{ "key": "photo.jpg", "size": 10485760, "etag": "\"abc123\"", "cached": true, "synced": true, "lastAccess": "2026-03-19T12:00:00Z" }
```

Fields:
- `cached`: true if the file exists in the local cache.
- `synced`: true if the file has been confirmed pushed to durable.
- `lastAccess`: updated on every read (for LRU eviction).

The manifest is the source of truth for `ListObjectsAsync` — it returns all known objects regardless of cache state. The manifest is populated from:
- Local writes (cached=true, synced=false until push completes).
- Pull-through (cached=true, synced=true).
- Remote-only discovery: on first connect to durable, enumerate remote objects and merge into manifest (cached=false, synced=true). This is lazy — triggered by first `ListObjects` call or background sync.

### 4. Sync journal

Append-only log of pending operations at `.Koan/storage-sync/{container}/journal.jsonl`.

Entry types:
- `put`: file written locally, needs push to durable. Content available in cache.
- `delete`: file deleted locally, needs delete on durable.

The background sync task:
- Runs continuously with configurable interval (default 5 seconds).
- Drains entries in order. For `put`: reads from cache, writes to durable. For `delete`: deletes from durable.
- On successful `put`: updates manifest (synced=true). File is now eviction-eligible.
- On failure: retries with exponential backoff. Entry stays in journal.
- On conflict detection (see §7): emits event, applies resolution policy.
- Journal compaction: after successful drain, truncate journal.

Staging directory `.Koan/storage-sync/{container}/staging/` is NOT used. Content for pending puts lives in the cache provider itself — no duplication.

### 5. Cache eviction

Only applies in `replicated` mode. Only evicts files where manifest has `synced=true`.

**Watermark policy**:
- `HighWatermark`: start evicting when cache usage exceeds this percentage of quota (default 90%).
- `LowWatermark`: stop evicting when cache usage drops to this percentage (default 70%).
- Eviction runs as a batch when high watermark is crossed, not per-operation.

**Eviction order**: LRU based on `lastAccess` in manifest.

**Eviction action**: delete local file via `cache.DeleteAsync()`, set `cached=false` in manifest. File remains in manifest (known to exist on durable). Next read triggers pull-through.

**Files NOT eligible for eviction**:
- `synced=false`: not yet pushed to durable. Evicting would lose data.
- Files with pending journal entries (put or delete in flight).
- Pinned containers (if `LocalCache.Policy: "pinned"` is set for the profile).

**Quota exceeded during disconnection**: if durable is unreachable, files accumulate locally because they can't be synced and therefore can't be evicted. This is correct — exceeding quota is safer than losing data. The quota is a target, not a hard limit.

Configuration:

```json
"Profiles": {
  "photos": {
    "Container": "photos",
    "LocalCache": {
      "MaxSize": "500MB",
      "HighWatermark": 90,
      "LowWatermark": 70,
      "Policy": "lru"
    }
  }
}
```

`LocalCache` absent = unlimited (no eviction, full local mirror).

### 6. Mode transitions

**Local → replicated**: when a remote provider becomes available (ZenGarden discovers storage), the framework upgrades the profile transparently. Existing local files are treated as unsynced — journal entries are created for all local files not yet in durable. Background sync pushes them.

**Replicated → local (degraded)**: when remote becomes unreachable, writes continue to local cache + journal. Reads serve from cache. Cache misses for remote-only files fail. Eviction pauses. On reconnect, sync resumes.

**Upgrade on reconnect**: when durable becomes reachable again after a disconnection, the sync task resumes draining the journal. No manual intervention.

### 7. Conflict detection

When the sync task pushes a file to durable, it checks if the remote already has a different version:
1. `HeadAsync` on durable before push.
2. If remote ETag differs from expected (neither absent nor matching), a conflict exists.

Resolution policies:
- `last-writer-wins` (default): overwrite remote with local. Simple, deterministic.
- `keep-remote`: discard local version, pull remote. For cases where remote is authoritative.
- `callback`: emit `ConflictDetected` event. App provides resolution.

Conflicts are logged. `ConflictDetected(container, key, localEtag, remoteEtag)` event emitted on the storage event channel.

### 8. Storage events

`ReplicatedStorageProvider` emits events through the existing storage event infrastructure:

| Event | When |
|---|---|
| `FilePushed(container, key)` | Successfully synced to durable |
| `FilePulled(container, key)` | Fetched from durable on cache miss |
| `FileEvicted(container, key)` | Removed from local cache |
| `ConflictDetected(container, key, localEtag, remoteEtag)` | Divergent state detected |
| `ModeChanged(profile, oldMode, newMode)` | Profile upgraded/degraded |

Apps subscribe via `StorageService.OnStorageEvent(handler)`. Events are opt-in — no overhead if no subscribers.

### 9. StorageService composition

`StorageService` is responsible for composing the right provider stack per profile. The logic:

1. Read profile config (container, mode, cache settings).
2. If `Mode` is explicit, use it.
3. If `Mode` is absent (auto):
   a. Collect registered `IStorageProvider` implementations.
   b. If only one provider registered → use directly.
   c. If local + remote both registered:
      - Remote reachable → `replicated` mode, compose `ReplicatedStorageProvider(cache: local, durable: remote)`.
      - Remote unreachable → `local` mode with background retry for upgrade.
   d. If only remote registered → `remote` mode.
4. Apply cache settings if `replicated`.

`StorageService` does NOT import `Koan.Storage.Connector.S3` or `Koan.Storage.Connector.Local`. It works entirely through `IStorageProvider` interfaces. Provider selection is by name or by capability, not by type.

### 10. Separation of concerns

| Component | Scope | Does NOT know about |
|---|---|---|
| `IStorageProvider` | Single-backend I/O | Caching, replication, eviction |
| `LocalStorageProvider` | Filesystem I/O | S3, remote, caching |
| `S3StorageProvider` | S3 protocol | Local, caching, eviction |
| `ReplicatedStorageProvider` | Two-provider composition, sync, eviction | S3, filesystem, specific backends |
| `StorageService` | Profile routing, provider composition | Backend specifics |
| `StorageEntity<T>` | Developer DX | All infrastructure |
| Sync journal | Pending operations log | Eviction, provider specifics |
| Manifest | Object index across tiers | Sync, eviction logic |

No layer reaches into another's concerns. `ReplicatedStorageProvider` could compose two `S3StorageProvider` instances (geo-replication) or two `LocalStorageProvider` instances (mirrored disks) with zero code changes.

## Scope

In scope:
- `ReplicatedStorageProvider` with sync journal, pull-through, eviction
- Local manifest for cross-tier object index
- Auto-detection of storage mode from registered providers
- Watermark-based LRU eviction with sync-eligibility guard
- Conflict detection with configurable resolution policy
- Storage events for replication lifecycle
- Streaming pull-through for large files
- `StorageService` composition logic

Out of scope:
- Content-addressable dedup (optimization, not correctness — deferred)
- Multi-remote replication (single durable provider per profile for now)
- Delta/incremental sync for large files
- Bandwidth throttling for background sync
- Compression in transit between tiers

## Consequences

### Positive
- Developer experience: `{ "Container": "photos" }` just works. Local when solo, replicated when garden is available.
- Container resilience: destroy and recreate, data pulls through from remote on demand.
- No data loss: eviction only when durable copy confirmed. Quota exceeded is safer than data lost.
- Provider agnostic: any `IStorageProvider` pair composes. Not tied to S3.
- Transparent upgrade: local → replicated when remote appears. No restart needed.

### Negative
- Local cache uses disk space that may exceed configured quota during disconnection.
- Manifest adds a small per-file bookkeeping overhead.
- First read after eviction has latency (pull from remote).
- Background sync task consumes resources (network, CPU for hashing).

### Risks
- Manifest corruption: mitigated by append-only format with periodic compaction and rebuild-from-scan capability.
- Sync journal unbounded growth during extended disconnection: mitigated by monitoring + alerts (storage events).
- Cache thrashing under high write + small quota: mitigated by watermark gap (default 20% between high and low).
- Conflict resolution in multi-instance: `last-writer-wins` may silently discard data. Mitigated by conflict events and configurable policy.

## References

- STOR-0005: LocalStorageProvider (cache tier baseline)
- STOR-0006: Storage default routing and fallbacks
- STOR-0009: Garden-aware S3 storage connector (durable tier)
- Zen Garden STORAGE-0016: Unified S3 storage gateway (remote storage surface)
- Rclone VFS cache: cache modes, polling, eviction (prior art)
- macOS iCloud: evictable files with cloud backing, local manifest (prior art)
- Windows CfApi: placeholder hydration (prior art, used by Moss cloud drive)
