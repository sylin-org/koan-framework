---
id: STOR-0002
slug: STOR-0002-storage-http-endpoints-and-semantics
domain: STOR
title: Storage HTTP endpoints, headers, and semantics (Koan.Web.Storage)
status: Accepted
date: 2025-08-24
---

Context

- Expose RESTful storage endpoints via controllers (no inline endpoints), keeping HTTP concerns out of Koan.Storage core.
- Support metadata, content streaming, downloads, tagging, and transfers with correct HTTP caching and range semantics.

Decision

- Create Koan.Web.Storage with attribute-routed controllers and transformers per WEB-0035 for payload shaping.
- Primary controller: StorageController<T> where T : IEntity, IStorageObject.
- Routes (indicative):
  - POST /storage - create + upload (multipart or streamed body). Return 201 (sync) or 202 (async) with Id.
  - GET /storage/{id} - metadata; supports ETag/If-None-Match.
  - GET /storage/{id}/content - binary content; supports Range (206) when provider is seekable; otherwise 200 with Accept-Ranges: none.
  - GET /storage/{id}/download - force download with Content-Disposition: attachment; filename normalization and safe fallback.
  - PATCH /storage/{id}/tags - add/remove tags idempotently.
  - POST /storage/{id}/transfer?profile=X - initiate transfer; returns 202 if async or 200 upon completion.
  - DELETE /storage/{id} - soft or hard delete per policy.
- Status/headers
  - ETag represents entity concurrency token (not necessarily ContentHash).
  - Accept-Ranges: bytes only when seek/range supported.
  - Quarantine/Pending access semantics via X-Storage-Status and 423 Locked (or 409) with reason code header X-Policy-Reason.
  - 415 for MIME mismatch; 413 for size limits; 412/304 for preconditions; 416 for unsupported ranges.
- Security
  - AuthZ policies per route; optional presigned URL redirects (302) for large downloads when enabled by profile.

Scope

- In scope: controller routes, HTTP status/headers, integration with orchestrator and router, download semantics, tag and transfer operations.
- Out of scope: resumable uploads/chunking (future), WebSockets; these can be added later with compatible routes.

Implementation notes

- End-to-end streaming; avoid buffering entire files in memory.
- Conditional GET and range handling must rely on provider capability flags and OpenRange fallbacks.
- Transformers (WEB-0035) may project metadata; content endpoints are binary-only.
- Constants for header names and route segments live under Koan.Web.Storage.Infrastructure.Constants.

Consequences

- Positive: clear HTTP surface with correct caching/partial content semantics, decoupled from core.
- Negative: requires coordination with core capabilities to signal range/presign behavior.

Follow-ups

- Wire Swagger annotations in Koan.Web.Swagger when Koan.Web.Storage is present.
- Provide small examples in src/Koan.Web.Storage/README.md.

References

- ARCH-0040 Config and constants naming
- WEB-0035 EntityController transformers
- STOR-0001 Storage module and contracts
