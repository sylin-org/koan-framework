---
## Summary

S7 TechDocs is a lightweight documentation/knowledge hub that showcases core Koan capabilities with a clear role-based permission model. It emphasizes clean, controller-based HTTP APIs, first-class data-access statics, deny-by-default capability enforcement, Postgres storage (FTS + pgvector), and AI-assisted authoring via an adapter-friendly abstraction. The UI is minimal (single-page, static) to keep attention on Koan's server-side design, testability, and ops posture. **No live collaboration features** - this is a focused demo of Koan's modular architecture.

### Role model
- **Readers**: All users can view published content, search, and browse collections.
- **Authors**: Can create drafts, edit their own content, submit for moderation, and use AI assists.
- **Moderators**: Can approve/reject/return submissions, manage any content, perform soft-delete/restore, and access audit trails.le: S7 TechDocs (realigned) - Proposal: Postgres, AI, Vector
description: A focused, no-collab initial release to demonstrate Koan’s modular capabilities using Postgres storage, full-text + vector search, and AI-assisted authoring.
---

## Summary

S7 TechDocs is a lightweight documentation/knowledge hub that showcases core Koan capabilities. It emphasizes clean, controller-based HTTP APIs, first-class data-access statics, deny-by-default capability enforcement, Postgres storage (FTS + pgvector), and AI-assisted authoring via an adapter-friendly abstraction. The UI is minimal (single-page, static) to keep attention on Koan’s server-side design, testability, and ops posture.

## Scope and non-goals

- In scope (Phase 1–2):
  - Markdown content with attachments; draft → submit → approve → publish.
  - Capability-backed operations: Moderation, SoftDelete, Audit.
  - Search: keyword (Postgres FTS) + semantic (pgvector) with filters/facets.
  - AI assists: tag inference, title/summary, outline/TOC suggestions, quality checks.
  - Minimal single-page UI hosted by the API for demo purposes.
- Not in scope (explicit exclusions):
  - Real-time collaboration (live co-editing, presence indicators, operational transforms, CRDTs).
  - Comments/annotations, complex multi-step approval workflows, WYSIWYG authoring.
  - External identity providers (using Koan.Web.Auth.TestProvider only).

## Contract (service-level)

- Inputs
  - Authenticated HTTP requests; JSON payloads for content and metadata.
  - Markdown text, optional attachments (binary), tags, visibility flags.
  - Optional AI prompts/settings for assistive features.
- Outputs
  - JSON resources (ProblemDetails for errors) with stable IDs, version metadata, and paging headers on list endpoints.
  - Search results with relevance scores; semantic results include vector similarity.
- Options
  - CapabilityAuthorizationOptions (deny-by-default), paging defaults, Postgres connection options, AI provider selection, embedding model and dimensions, thresholds for similarity.
- Error modes
  - Validation errors (400), authn/authz denials (401/403 with ProblemDetails), conflicts (409 for version race), not found (404), server errors (500 with activity id).
- Success criteria
  - Endpoints return 2xx, include paging headers when applicable, and adhere to controller-based routing. Data access uses first-class model statics.

## Feature set

- Content model
  - Collection → Item → Version (append-only), Tags, Attachments, Sections (optional logical headings), Status (Draft/Submitted/Approved/Published/Archived).
  - Snapshots via the Audit capability; Soft-delete and restore for Items and Versions.
- Authoring
  - Markdown with front matter for title/description/tags; server-side markdown parsing for preview and TOC extraction (no code in this doc; implementation detail).
  - Workflow: draft (Authors) → submit (Authors) → moderation queue → approve/reject/return (Moderators) → publish.
  - Authors can edit their own drafts; Moderators can edit any content at any stage.
- Capabilities and permissions
  - Moderation: queue access, approve/reject/return (Moderators only).
  - SoftDelete: mark/restore items and versions (Moderators only).
  - Audit: list snapshots and revert changes (Moderators only).
  - Content creation: draft, edit own content, submit for review (Authors and Moderators).
  - Content viewing: browse published content, search, view collections (all Readers).
  - Deny-by-default: explicit capability mappings required per role.
- Search and discovery
  - Keyword: Postgres full-text search (tsvector/tsquery) across title, body, tags.
  - Semantic: pgvector embeddings on canonicalized text; ANN index (IVFFlat/HNSW) with k-NN search; optional hybrid scoring (BM25 + cosine similarity).
  - Filters: collection, tag, status, date; facets (top tags, collections).
  - “Related content” recommendations based on vector similarity within collection.
- AI assists (adapter-agnostic)
  - Tag inference, title/summary, outline/TOC, readability/quality checks with suggestions.
  - Batch/offline enrichment for backlog; on-demand assist for active drafts.
  - Pluggable provider (e.g., local Ollama) behind Koan AI abstraction; prompts and safety rules centralized.
- UI
  - Minimal, static SPA under the API (e.g., /ui) with list/search/view and simple authoring forms.
  - No real-time features: no live co-editing, presence indicators, or collaborative cursors.
  - Simple form-based content creation and editing only.
- Observability
  - Request logging, metrics (p50/p95), audit trails via capability hooks; basic health and readiness endpoints.

## Technical specifications

- Storage: Postgres (15+ recommended)

  - Extensions: pgvector (>= 0.5), pg_trgm (for similarity), and standard FTS.
  - Core tables (logical outline)
    - collections(id, slug, name, created_at, created_by)
    - items(id, collection_id, slug, latest_version_id, status, tags[], is_deleted, created_at, created_by)
    - item_versions(id, item_id, version_no, title, markdown, html, tags[], created_at, created_by)
    - attachments(id, item_id, version_id, name, media_type, size_bytes, url|blob, created_at)
    - audits(id, entity_type, entity_id, operation, snapshot_json, created_at, created_by)
    - embeddings(id, entity_type, entity_id, vector, dim, model, created_at)
    - users(id, username, email, roles[], created_at) -- roles: ["Reader"], ["Reader", "Author"], ["Reader", "Author", "Moderator"]
    - user_sessions(id, user_id, token_hash, expires_at, created_at) -- managed by Koan.Web.Auth.TestProvider
  - Views/materializations
    - latest_item_versions view for fast “current content” reads.
  - Indexing
    - GIN on to_tsvector(title, html/plaintext, tags) for FTS.
    - pgvector index (IVFFlat/HNSW) on embeddings.vector with appropriate lists/probes.
    - Trigram indexes on slug/name for fuzzy matches.
  - Transactions and concurrency
    - Optimistic concurrency with version_no; 409 on conflicting writes.
  - Pagination
    - Offset + limit with stable sort; consistent defaults from centralized constants; align with DATA-0061.
  - Tenancy
    - Single-tenant for demo; leave space for tenant_id in all tables for future.
  - Backups and migrations
    - SQL migrations committed in repo, idempotent and ordered; startup gate to apply when enabled.
    - Daily logical backups and WAL archiving (environmental concern, not enforced by app).

- Search pipeline

  - Ingest path updates FTS documents and schedules embedding generation for drafts and published versions.
  - Hybrid search endpoint accepts q (text), k (limit), filters, and returns combined relevance with explain metadata (optional).

- AI pipeline

  - Provider abstraction with configurable model (embedding + generation), rate limits, and timeouts.
  - Trigger modes: on-create (async), on-demand (user-initiated), and batch (nightly).
  - Safety: prompt guardrails and max token limits; redaction hooks for sensitive fields.

- Web/API conventions

  - Attribute-routed MVC controllers only; no inline endpoints.
  - ProblemDetails for errors; activity id correlation in responses.
  - Role-based capability mapping: Reader → view published content, search; Author → create/edit own drafts, submit, AI assists; Moderator → all Author capabilities + approve/reject/return, soft-delete/restore, audit access.
  - Default deny for all capability-protected endpoints.

- Configuration and constants

  - Centralized Constants class for routes, header names (paging), default page size, and media types.
  - Options-bound settings for Postgres (connection string, pool sizes), AI provider, and search thresholds.

- Security

  - Auth: Koan.Web.Auth.TestProvider (as used in S5) for demo authentication with role-based claims.
  - Role hierarchy: Reader (baseline) ⊆ Author ⊆ Moderator (additive permissions).
  - Content ownership: Authors can edit their own drafts; Moderators can edit any content.
  - Capability enforcement: publish/approve restricted to Moderators; draft creation/editing to Authors+; viewing to all Readers.

- Operations
  - Healthz/readyz; startup checks for Postgres connectivity and required extensions (pgvector/pg_trgm); background worker liveness (embedding queue).

## Role-based permissions matrix

| Action                         | Reader | Author  | Moderator |
| ------------------------------ | ------ | ------- | --------- |
| View published content         | ✓      | ✓       | ✓         |
| Search (FTS + semantic)        | ✓      | ✓       | ✓         |
| Browse collections             | ✓      | ✓       | ✓         |
| Create drafts                  | ✗      | ✓       | ✓         |
| Edit own drafts                | ✗      | ✓       | ✓         |
| Edit any content               | ✗      | ✗       | ✓         |
| Submit for moderation          | ✗      | ✓       | ✓         |
| Access moderation queue        | ✗      | ✗       | ✓         |
| Approve/reject/return          | ✗      | ✗       | ✓         |
| Soft-delete/restore            | ✗      | ✗       | ✓         |
| Access audit trails            | ✗      | ✗       | ✓         |
| Revert from audit              | ✗      | ✗       | ✓         |
| AI assists (tags/summary/etc.) | ✗      | ✓       | ✓         |
| Manage attachments             | ✗      | ✓ (own) | ✓ (any)   |

### Capability mappings

- **View capabilities**: All users (no explicit capability required for published content)
- **Author capabilities**: `Content.Create`, `Content.EditOwn`, `Content.Submit`, `AI.Assist`
- **Moderator capabilities**: All Author capabilities + `Moderation.Queue`, `Moderation.Approve`, `Moderation.Reject`, `Moderation.Return`, `SoftDelete.Mark`, `SoftDelete.Restore`, `Audit.List`, `Audit.Revert`, `Content.EditAny`

## Edge cases to handle

- Empty/huge documents; binary attachments too large or unsupported media types.
- Long-running AI calls, timeouts, or provider unavailability (degrade gracefully; retry/backoff).
- Vector dimension mismatch after model change (version vectors and re-embed as needed).
- FTS/semantic mismatch for languages; ensure language configuration or fallback.
- Concurrent edits across versions; publish race (use version_no and transactions).

## Phased delivery plan

- Phase 0: Foundations (1–2 days)
  - Postgres schema and extensions provisioned; migrations committed.
  - Koan.Web.Auth.TestProvider integration (following S5 pattern); role-based claims.
  - Constants/options scaffolding; health endpoints; deny-by-default capabilities wired.
- Phase 1: MVP (3–5 days)
  - CRUD for collections/items/versions; draft → submit → approve → publish flow.
  - FTS search, list/paging, attachments metadata; minimal UI under /ui.
  - Observability and ProblemDetails baselines.
- Phase 2: AI + Vector (3–5 days)
  - Embedding generation (async), pgvector index, semantic and hybrid search.
  - AI assists for tags/title/summary/outline; batch enrich job.
- Phase 3: Hardening (2–3 days)
  - Role-based capability mapping implementation; audit snapshots and revert; soft-delete/restore.
  - Content ownership enforcement (Authors edit own, Moderators edit any).
  - Caching, index tuning, edges and large documents.

## Acceptance checks (high level)

- Build, lint, and strict docs build pass; controllers expose only attribute-routed endpoints.
- Tests for deny-by-default on capability endpoints; ProblemDetails shape verified.
- Role-based authorization tests: Readers denied Author/Moderator actions; Authors denied Moderator actions; content ownership enforced.
- FTS returns relevant results for seeded data; semantic search returns similar documents with stable k-NN performance.
- AI assists produce tags/summaries for sample docs; failures degrade gracefully.
- Paging headers present; constants referenced (no magic values).

## Out of scope (explicit exclusions)

- Real-time collaboration: live co-editing, operational transforms (OT), conflict-free replicated data types (CRDTs).
- Presence features: online indicators, shared cursors, user avatars, live activity feeds.
- Advanced collaboration: inline comments, suggestions, threaded discussions, review workflows.
- External integrations: third-party identity providers, webhook-based notifications, API-driven sync.

## References

- Engineering front door: /docs/engineering/index.md
- Architecture principles: /docs/architecture/principles.md
- Data access patterns: /docs/guides/data/all-query-streaming-and-pager.md, /docs/decisions/DATA-0061-data-access-pagination-and-streaming.md
- Web API conventions: /docs/api/web-http-api.md, /docs/decisions/WEB-0035-entitycontroller-transformers.md
- Storage reference: /docs/reference/storage.md; AI reference: /docs/reference/ai.md
- Authentication pattern: samples/S5.Recs (Koan.Web.Auth.TestProvider usage example)
