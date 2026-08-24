---
type: REFERENCE
domain: storage
title: "Media derivatives"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: passed
  scope: docs/capabilities/state/media-derivatives.md - cold-executed via accept-and-serve-files on
    the local path: upload, named derivative served twice from equivalent recipe terminals with
    matching fingerprints; recipe gaps found in the run were fixed the same day
---

# Media derivatives

Keep one `MediaEntity<T>` original and expose named, reproducible image variants without creating a
second domain record for every size or format.

## You need

| Piece | Package | Note |
|---|---|---|
| Media Entity and recipe runtime | `Sylin.Koan.Media.Core` | owns `MediaEntity<T>` and recipe discovery |
| Bounded HTTP projection | `Sylin.Koan.Media.Web` | serves original and named derivatives |
| Somewhere to keep originals | one Storage connector | Media brings Storage, not a concrete byte provider |
| Durable ingest (optional) | `Sylin.Koan.Jobs` | use when upload processing must survive restart |

## The constraint box

> **The constraint:** The original is the record and derivatives are reproducible; transforms must
> never overwrite the original. Media Core is in-process, not a durable rendering job system, and a
> recipe alone does not add scheduling, orphan cleanup, access policy, malware scanning, or ingress
> bounds.

## Choose when work happens

| Shape | Use when | Cost |
|---|---|---|
| Derive on first request | variants may never be viewed and first-view latency is acceptable | request pays CPU once; result may be retained |
| Process during ingest | the first view must be fast or AI enrichment also runs | stage bytes, return a Job receipt, delete staging only after success |
| Store only unchanged files | no derivative is required | use [Entity-owned files](entity-files.md) without Media |

## Leaves

- **Build and delivery proof:** [accept and serve files](../../recipes/accept-and-serve-files.md)
- **Compound blueprint; choose and prove the AI providers:** [photo pipeline](../../recipes/photo-pipeline.md)
- **Recipe contract:** [media guide](../../guides/media-recipes-howto.md)
- **Package contract:** runtime mechanics:
  [Media Core README](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.Media.Core/README.md)

To make the original searchable by its contents, add the inherited model/index constraint from
[semantic search](../ai/semantic-search.md).
