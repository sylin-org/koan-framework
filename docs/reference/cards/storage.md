---
type: REF
domain: storage
title: "Storage — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-06-18
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-18
  status: verified
  scope: docs/reference/cards/storage.md
---

# Storage — pillar map

> One-screen map of the Storage pillar — named profiles route bytes to pluggable providers, with streaming read/write and entity-bound files. Full detail: [Storage Pillar Reference](../storage/index.md).

**What it does** — Storage is a thin orchestration seam (`IStorageService`) over named **profiles**, where each profile maps to a provider + container. Code names a profile, never a vendor: the same `Put`/`Read`/`ReadRange`/`Delete`/`TransferToProfile` calls run against local disk or S3 unchanged. Provider activation is **Reference = Intent** — referencing `Koan.Storage.Connector.Local` or `Koan.Storage.Connector.S3` activates that package's `KoanModule`, no manual wiring ([STOR-0001](../../decisions/STOR-0001-storage-module-and-contracts.md), [STOR-0005](../../decisions/STOR-0005-storage-local-provider.md), [STOR-0009](../../decisions/STOR-0009-garden-s3-storage-connector.md)). A profile can run `Local`, `Remote`, or `Replicated` (local cache with async push to a durable remote) via `StorageMode` ([STOR-0010](../../decisions/STOR-0010-replicated-storage-with-local-cache.md)).

## The one canonical pattern

Bind an entity to a profile with `[StorageBinding]`, then onboard a stream and read it back through the entity's own helpers — the profile/container resolve from the attribute.

```csharp
[StorageBinding(Profile = "cold", Container = "photos")]
public sealed class PhotoAsset : StorageEntity<PhotoAsset>
{
    public string EventId { get; set; } = "";
}

// Onboard a stream (resolves profile+container from the binding; streams, never buffers)
var photo = await PhotoAsset.Onboard("sunset.jpg", uploadStream, "image/jpeg");
await photo.Save();                                   // persist the metadata row (entity verb)

// Read back through the bound entity
await using var s = await photo.OpenRead();           // full stream
var (range, len) = await photo.OpenReadRange(0, 1023); // first 1 KiB
var stat = await photo.Head();                         // size/content-type without a full read
await photo.Delete();                                  // remove the blob
```

Move a file across profiles (tiering) with `await photo.MoveTo<ColdPhoto>()`; copy with `CopyTo<T>()`.

## ≤5 attributes you'll use

| Attribute / option | What it does |
|---|---|
| `[StorageBinding(Profile, Container)]` | Binds a `StorageEntity<T>` to a named profile + container so static/instance helpers resolve their target automatically. |
| `StorageOptions.Profiles["name"]` | Declares a profile: `Provider` (null = auto-detect), `Container`, optional `Mode`, optional `LocalCache`. |
| `StorageOptions.DefaultProfile` | The profile used when a caller passes no profile name. |
| `StorageFallbackMode` | `Disabled` / `SingleProfileOnly` (use the sole registered profile) / `NamedDefault` when no profile is specified. |
| `StorageMode` | Per-profile routing: `Local`, `Remote`, or `Replicated` (cache + async push, pull-through on miss). |

Config section is `Koan:Storage` (`StorageConstants.Constants.Configuration.Section`).

## The escape hatch

Drop to `IStorageService` directly for raw streaming, presigning, range reads, server-side copy, and listing — addressing `(profile, container, key)` explicitly without an entity. Capability flags (`StorageProviderCapabilities`) gate the optional surfaces.

```csharp
var storage = sp.GetRequiredService<IStorageService>();
var obj = await storage.Put("hot", "uploads", key, stream, "application/pdf");
var (chunk, length) = await storage.ReadRange("hot", "uploads", key, from: 0, to: 4095);
var url = await storage.PresignRead("hot", "uploads", key, TimeSpan.FromMinutes(15)); // S3 only
await foreach (var info in storage.ListObjects("hot", "uploads", prefix: "2026/"))
    Console.WriteLine(info.Key);
// Fluent routing sugar: storage.InProfile("cold", "photos").Onboard(key, stream)
```

## The sample that shows it

[`samples/S6.SnapVault`](../../../samples/S6.SnapVault/README.md) — a photo-management app whose `PhotoAsset : MediaEntity<PhotoAsset>` carries `[StorageBinding(Profile = "cold", Container = "photos")]` and onboards/reads image blobs through the storage seam (with derivative thumbnails on their own bindings).
