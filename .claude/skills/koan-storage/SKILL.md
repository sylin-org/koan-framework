---
name: koan-storage
description: Entity-bound blob storage over named profiles — StorageEntity<T> + [StorageBinding(Profile, Container)], Onboard/OpenRead/OpenReadRange/Head/Delete, MoveTo/CopyTo tiering, the IStorageService escape hatch, and Local/Remote/Replicated StorageMode routing
pillar: storage
card: docs/reference/cards/storage.md
status: current
last_validated: 2026-06-18
---

# Koan Storage

## Trigger this skill when you see

- `StorageEntity<T>` / `[StorageBinding(Profile = ..., Container = ...)]` on an entity
- `MyAsset.Onboard(name, stream, contentType)`, `entity.OpenRead()`, `.OpenReadRange(from, to)`, `.Head()`, `.Delete()`
- `entity.MoveTo<TCold>()` / `entity.CopyTo<TCold>()` — moving a blob across storage tiers
- `IStorageService` directly — `Put`/`Read`/`ReadRange`/`Delete`/`Head`/`TransferToProfile`/`PresignRead`/`ListObjects`, or `storage.InProfile("hot", "uploads")`
- References to `Koan.Storage` / `Koan.Storage.Connector.Local` / `Koan.Storage.Connector.S3`
- `StorageOptions.Profiles[...]`, `StorageMode` (`Local`/`Remote`/`Replicated`), `StorageFallbackMode`, the `Koan:Storage` config section
- "blob storage", "object storage", "file upload", "presigned URL", "S3", "cold/hot tier", "stream a file", "range read", "byte range"

## Core principle

**Name a profile, never a vendor.** Storage is a thin orchestration seam (`IStorageService`) over named **profiles**; each profile maps to a provider + container. The same `Onboard`/`OpenRead`/`Head`/`Delete` calls run against local disk or S3 unchanged. Bind an `Entity<T>` to a profile with `[StorageBinding]` and the entity's own helpers resolve their target from the attribute — the metadata row persists with `Save()` like any entity, while the bytes stream through the seam (never buffered). Provider activation is **Reference = Intent**: a connector's `KoanModule` contributes its `IStorageProvider`; application code adds no provider-registration list.

<!-- validate -->
```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Koan.Storage.Abstractions;
using Koan.Storage;
using Koan.Storage.Model;

[StorageBinding(Profile = "cold", Container = "photos")]
public sealed class PhotoAsset : StorageEntity<PhotoAsset>
{
    public string EventId { get; set; } = "";
}

// A "hot" tier the same blob can be promoted to
[StorageBinding(Profile = "hot", Container = "photos")]
public sealed class HotPhoto : StorageEntity<HotPhoto>;

public sealed class PhotoIntake
{
    public async Task<PhotoAsset> Ingest(Stream upload, CancellationToken ct = default)
    {
        // Onboard resolves profile+container from [StorageBinding]; streams, never buffers
        var photo = await PhotoAsset.Onboard("sunset.jpg", upload, "image/jpeg", ct);
        await photo.Save(ct);                                  // persist the metadata row (entity verb)
        return photo;
    }

    public async Task Serve(PhotoAsset photo, CancellationToken ct = default)
    {
        await using var full = await photo.OpenRead(ct);                 // full stream
        var (range, length) = await photo.OpenReadRange(0, 1023, ct);    // first 1 KiB
        ObjectStat? stat = await photo.Head(ct);                         // size/content-type, no full read
        _ = (full, range, length, stat);
    }

    public Task<HotPhoto> Promote(PhotoAsset photo, CancellationToken ct = default)
        => photo.MoveTo<HotPhoto>(ct);                         // tier across profiles (CopyTo<T>() to keep both)
}
```

## Reference = Intent activation

| Add this reference | Effect |
|---|---|
| `Koan.Storage` | The seam (`IStorageService`), `StorageEntity<T>`, `[StorageBinding]`, options. No provider yet. |
| `+ Koan.Storage.Connector.Local` | Registers a local-filesystem `IStorageProvider` (sequential + seek/range) ([STOR-0005](../../../docs/decisions/STOR-0005-storage-local-provider.md)). |
| `+ Koan.Storage.Connector.S3` | Registers an S3-compatible provider — adds presigned URLs + server-side copy ([STOR-0009](../../../docs/decisions/STOR-0009-garden-s3-storage-connector.md)). |
| `StorageMode.Replicated` on a profile | Local cache with async push to a durable remote; pull-through on miss ([STOR-0010](../../../docs/decisions/STOR-0010-replicated-storage-with-local-cache.md)). |

Profiles live under the `Koan:Storage` config section (`StorageConstants.Constants.Configuration.Section`):

```jsonc
// appsettings.json
"Koan": { "Storage": {
  "DefaultProfile": "cold",
  "FallbackMode": "NamedDefault",                 // Disabled | SingleProfileOnly | NamedDefault
  "Profiles": {
    "cold": { "Provider": "s3",    "Container": "photos", "Mode": "Remote" },
    "hot":  { "Provider": null,    "Container": "photos", "Mode": "Local" }   // null = auto-detect
  }
}}
```

`StorageMode` is per-profile routing: `Local` (disk only), `Remote` (durable only), `Replicated` (local cache + async push, pull-through on miss). `StorageFallbackMode` decides what happens when a caller names no profile: `Disabled`, `SingleProfileOnly` (use the sole registered profile), or `NamedDefault` (use `DefaultProfile`).

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `new S3Client(...)` / `File.OpenRead(path)` threaded through business code | `[StorageBinding]` on a `StorageEntity<T>` + `Onboard`/`OpenRead` — name a profile, not a vendor. |
| A hardcoded `"s3"` / `"local"` vendor string at the call site | Name a **profile** (`"cold"`/`"hot"`); the profile maps to a provider in config, so swapping disk↔S3 is config-only. |
| `await stream.CopyToAsync(ms)` to buffer an upload before saving | `Onboard(name, stream, contentType)` streams straight through — never buffer a whole blob. |
| `OpenRead()` then reading just the first N bytes | `OpenReadRange(from, to)` / `IStorageService.ReadRange(...)` — range read, no full transfer. |
| `OpenRead()` just to get the size or content-type | `Head()` → `ObjectStat` (size/content-type/etag) without a body read. |
| Download-then-reupload to move a blob between tiers | `entity.MoveTo<TCold>()` / `CopyTo<TCold>()` — server-side copy when the provider supports it, streamed otherwise. |
| `services.AddSingleton<IStorageProvider>(...)` by hand | Reference `Koan.Storage.Connector.Local`/`.S3`; the registrar wires the provider (Reference = Intent). |
| Building a download URL by hand / proxying bytes through your API for a public link | `IStorageService.PresignRead(profile, container, key, expiry)` (S3 capability). |

## Escape hatches

- **`IStorageService` directly** — raw streaming addressed by `(profile, container, key)` without an entity. `Put` / `Read` / `ReadRange` / `Delete` / `Head` / `Exists` / `TransferToProfile`, plus capability-gated `PresignRead` / `PresignWrite` / `ListObjects`:
  ```csharp
  var storage = sp.GetRequiredService<IStorageService>();
  var obj = await storage.Put("hot", "uploads", key, stream, "application/pdf");
  var (chunk, length) = await storage.ReadRange("hot", "uploads", key, from: 0, to: 4095);
  var url = await storage.PresignRead("hot", "uploads", key, TimeSpan.FromMinutes(15)); // S3 only
  await foreach (var info in storage.ListObjects("hot", "uploads", prefix: "2026/"))
      _ = info.Key;
  ```
- **Fluent routing sugar** — `storage.InProfile("cold", "photos").Onboard(key, stream)` binds a profile/container once for a run of calls (`ProfiledStorage`).
- **Capability gating** — `StorageProviderCapabilities` (`SupportsSeek`, `SupportsPresignedRead`, `SupportsServerSideCopy`) tells you which optional surfaces a provider offers; presign/range degrade by provider (local disk has no presign).
- **Key/proxy helpers** — `StorageEntity<T>.Get(key)` builds a lightweight proxy to `OpenRead()`/`Head()` by key alone; `StorageEntity<T>.CreateTextFile`/`Create`/`Create<TDoc>(...)` onboard inline text/bytes/JSON.

## See also

- [Reference card: storage.md](../../../docs/reference/cards/storage.md) — one-screen pillar map
- [Storage pillar reference](../../../docs/reference/storage/index.md) — full detail (profiles, providers, routing)
- [STOR-0001 — storage module and contracts](../../../docs/decisions/STOR-0001-storage-module-and-contracts.md)
- [STOR-0010 — replicated storage with local cache](../../../docs/decisions/STOR-0010-replicated-storage-with-local-cache.md)
- [SnapVault](../../../samples/applications/SnapVault/README.md) — photo app whose `PhotoAsset` carries `[StorageBinding(Profile = "cold", Container = "photos")]` and onboards/reads image blobs through the seam
