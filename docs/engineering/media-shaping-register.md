---
type: ENGINEERING
domain: storage
title: "Media & storage shaping register"
audience: [maintainers, framework-authors, module-authors]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: reviewed
  scope: entries re-verified against dev source (a8e4043f9..48fe40014) on 2026-08-24; field evidence
    dates to mid-2026 against pre-1.0 packages and is treated as ergonomic signal, not a supported baseline
---

# Media & storage shaping register

Field frictions harvested from `gposingway/bundlingways-emporium.v2` (external production consumer,
pre-1.0 lineage), recorded as shaping candidates per the [capability directory](capability-directory.md)
§Field harvest, which owns the ranking. This page carries each candidate's **current tree state** so a
future session does not re-litigate what is already addressed. The consumer's own engineering register
(MEDIA-0004/0006/0007/0008) tracks several of these upstream — cross-linked below rather than duplicated.

## 1. Derivation lifecycle is app-owned — OPEN

Write-through plus orphan sweep was hand-rolled by the consumer (~7-file prewarm subsystem). Part of the
blocker persists in the tree: keyed create offers only byte/`ReadOnlyMemory<byte>` overloads — no
`Storage.Create(string, Stream)` (`src/Koan.Storage/Extensions/ProfiledStorage.cs`), which forced their
obsolete byte[] API use under `#pragma CS0618`. Candidate capability: framework-owned warm/render API +
sweep; the stream overload is the smallest unblocking step.

## 2. Prewarm via HTTP self-call — OPEN

No in-process warm API exists (grep-verified), so loopback draining remains the only route; their SSRF
egress guard fought it until disabled (their commit marked temporary). Same candidate capability as #1:
an in-process warm API deletes both problems.

## 3. Upload/Store persistence asymmetry — PARTIALLY ADDRESSED

`MediaEntity.Store(byte[])` now computes a SHA-256 key, dedupes, and **persists the row**
(`src/Koan.Media.Core/Model/MediaEntity.cs`, `await entity.Save(ct)`). `Upload(Stream, name)` rides
`StorageEntity.Onboard`, which writes the blob and returns an entity but **still never persists the
row** (`src/Koan.Storage/Model/StorageEntity.cs`) — the forgotten-`.Upsert()` hazard survives on that
path. Candidate fix unchanged: persist-on-upload default or a corrective warning fact naming the remedy.

## 4. Keyed store writes are not idempotent — PARTIALLY ADDRESSED

The content-addressed `Store` path dedups at both the entity layer and the storage layer. Direct keyed
creates (`ProfiledStorage.Create(key, …)`) keep their existing semantics — unverified whether a second
write still surfaces provider `IOException "already exists"` for callers to hand-handle.

## 5. Unpinnable ad-hoc transform slots — OPEN

Gallery/lightbox scenarios still need raw `?w=&fit=&format=&q=` slots that recipe prewarm cannot cover.
Candidate: documented pattern or first-class ad-hoc pinning. Related decision: MEDIA-0003
(variant routing) and MEDIA-0004 (recipe pipeline) own the surrounding surface.

## 6. Portable-key rule enforced but undocumented — OPEN (documentation gap)

The Local sanitizer deliberately rejects `< > : " | ? *` as portable-key characters with the comment
"keep the logical key language stable across hosts" (`src/Connectors/Storage/Local/LocalStorageProvider.cs`,
`PortableInvalidKeyCharacters`). The rule itself is the design; what is missing is its statement at the
provider level so applications learn the `__`-style convention from the docs instead of from a runtime
`InvalidOperationException`.

## 7. Lineage setters are `protected internal` — OPEN

`SourceMediaId`, `RelationshipType`, `DerivationKey`, `ThumbnailMediaId` all carry
`protected internal set` (`src/Koan.Media.Core/Model/MediaEntity.cs`), forcing duplicated stamp shims in
every entity subclass that needs to set lineage from outside the hierarchy. Candidate: an intentional
lineage-stamping surface or widened setter authority with a guard.

## 8. SVG pipeline cliff — CROSS-LINK

MEDIA-0006 (SVG decoder + Skia rasterizer) owns this area in-tree and postdates part of the field
evidence. Before shaping anything new, verify current-feed behavior against the sniff-and-bypass pattern
the consumer shipped; the entry closes if the ADR's path answers their validator/rasterizer needs.

---

Cross-links to the consumer's register are recorded here once; their MEDIA-0004/0006/0007/0008 items
correspond to frictions 5/8/4/1 respectively by topic and stay authoritative in their repository.
