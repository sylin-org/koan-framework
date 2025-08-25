# MEDIA-0001 — Media pillar baseline, tasked pipelines, and storage integration

Owners: Sora Media  
Status: Accepted  
Date: 2025-08-24

## Context

Modern apps need first-class media handling (images, video, audio, PDFs), including derivatives (thumbnails, crops, transcodes), text extraction and translation, and safe, cacheable delivery. We want enterprise-grade capabilities with premium DX, aligned with Sora’s model-first style and existing Storage, Web, Messaging, and Scheduling pillars.

## Decision

- Pillar and packages
  - Introduce Sora.Media with a clean split:
    - Sora.Media.Abstractions — contracts, events, transform specs
    - Sora.Media.Core — Ensure/RunTask, pipelines, ancestry, idempotency
    - Sora.Media.Storage — bridge to Sora.Storage (placement, URLs)
    - Sora.Media.Web — controllers and HTTP surface (no inline endpoints)
    - Sora.Media.Actions.* — Image/PDF/Video/Text action packs (opt-in heavy deps)

- First-class model statics (DX)
  - Media model pattern: `public sealed class MyMedia : MediaObject<MyMedia> {}`
  - Static methods (contract): `Upload`, `Get`, `Open`, `Url`, `Ensure`, `RunTask`, `Derivatives`, `Ancestors`, `Descendants`, `DescribeTask`, `GetTask`, `StreamTask`.
  - Derivatives are first-class media objects linked to their source.

- Named tasks (recipes)
  - Task codes follow `code@version` (e.g., `document-translation@1`), hoisted to `MyMedia.TaskCodes` constants.
  - Register via a task registry per model; steps are composable (e.g., ExtractText → Translate).
  - Invoke with `MyMedia.RunTask(mediaId, code, args?, track: true, ct)`. Instance sugar is optional; the static is canonical.
  - Arguments accepted as anonymous object or dictionary; normalized centrally for idempotency.

- DescribeTask schema (minimal)
  - `MediaTaskDescriptor { Code, Version, Title?, Summary?, Args[], Steps[], Requires[]? }`
  - `MediaTaskArg { Name, Type, Required, Default?, Allowed?, Min?, Max?, Pattern? }`
  - `MediaTaskStep { Name, Action, SpecTemplate, Relationship?, SavesTo?, ContinueOnError? }`
  - Placeholders like `${lang}` are resolved during normalization; ordering is stable.

- Tasked pipeline execution
  - Multi-step or tracked runs create a `TaskId` with status: Pending, Processing, Completed, Failed, Cancelled.
  - Persist `MediaTaskRecord` (task + steps timeline), expose `GetTask` and SSE `StreamTask`.
  - Idempotency: `TaskFingerprint = hash(SourceId + code + version + normalizedArgs)`; per-step derivatives use a `DerivationKey = hash(SourceId + normalizedStepSpec)`.

- Ancestry and relationships
  - Each derivative stores `SourceMediaId`, `DerivationKey`, and `RelationshipType` (e.g., `thumbnail`, `translate:pt-br`).
  - Provide upward (`Ancestors`) and downward (`Descendants`/`Derivatives`) traversal statics.

- Storage integration (must)
  - All blob placement and retrieval go through Sora.Storage profiles and routing.
  - Placement decisions can use content-type, size, and tags/metadata (e.g., `profile-photo`, `archive`, `pii`).
  - Media records carry `StorageKey`, `StorageProvider`, `ContentHash`, `ETag`; presigned URL generation is delegated to Sora.Storage providers.
  - CDN compatibility: support signed URLs, range requests, and cache headers; on-demand transforms require signed tokens or pre-registered variant policies.

## Rationale

- Matches Sora’s model-first, low-ceremony DX while leveraging existing pillars for durability (Storage), async (Scheduling/Messaging), and web semantics.
- Named tasks provide discoverable, auditable recipes with simple one-liners.
- Derivatives as first-class objects enable reuse, lifecycle, and clear security boundaries.
- Centralizing placement via Sora.Storage avoids duplicated logic and keeps providers thin.

## Consequences

- New abstractions/contracts for transforms, tasks, and ancestry graph.
- A small rules engine/pipeline builder in Core; heavy actions live in Actions.* packs.
- HTTP controller set for upload, variants, tasks, and task events; no inline endpoints.
- Need a normalization layer for args/specs to guarantee idempotency and safe signing.

## Out of scope (MVP)

- Video transcode packaging (HLS/DASH) and OCR are planned but not required in the first slice.
- Resumable uploads (tus) and task cancellation semantics can follow.
- CDN adapters (beyond presigned URL guidance) are optional initially.

## Next steps

- Implement Abstractions/Core scaffolding and register a minimal Image actions pack (thumbnail/resize) and PDF text extract.
- Wire `Upload/Ensure/RunTask/Url` statics and the task registry with the described schemas.
- Bridge to Sora.Storage routing using tags and content-type; document a few routing examples.
- Add controllers for `/media`, `/media/{id}/variants`, `/media/{id}/tasks/{code}`, and `/media/tasks/{taskId}`.
