# S7.TechDocs – De‑mock Plan and Implementation Checklist

Last updated: 2025-08-29

Purpose: track replacing mock behaviors with proper Koan-backed functionality. Keep this list authoritative for scope and progress.

References
- Engineering front door: /docs/engineering/index.md
- Architecture principles: /docs/architecture/principles.md
- Data pagination/streaming: /docs/decisions/DATA-0061-data-access-pagination-and-streaming.md
- Web payload shaping: /docs/decisions/WEB-0035-entitycontroller-transformers.md
- Data guides: /docs/guides/data/all-query-streaming-and-pager.md, /docs/guides/data/working-with-entity-data.md

## Package wiring (once)
- [ ] Add and configure (dev/prod as appropriate):
  - [ ] Koan.Web.Extensions, Koan.Web.Swagger
  - [ ] Koan.Web.Auth.TestProvider (dev), Koan.Web.Auth.Oidc (prod)
  - [ ] Koan.Data.Postgres, Koan.Data.Relational
  - [ ] Koan.Data.Vector (pgvector)
  - [ ] Koan.Messaging.Inbox.Http (dev) / Koan.Messaging.RabbitMq (prod)
  - [ ] Koan.AI, Koan.Ai.Provider.Ollama (dev) or selected provider
  - [ ] Koan.Recipe.Observability

---

## Phase 0 – Hardening and baselines
- [x] Enable Swagger UI
- [ ] Annotate responses (problem details, examples)
- [ ] Add health probes (DB, AI, messaging) via Koan.Recipe.Observability
- [x] Replace custom auth middleware with TestProvider in Development

## Phase 1 – Persistence (remove in-memory services)
- [x] Dev: Convert models to Entity<> and persist via JSON adapter (seeded dataset)
- [x] Replace services to use first-class model statics (All/Query/Get/Batch)
- [x] Ensure server-side filtering by role remains enforced (DB-backed via adapter)
- [ ] Create relational schema (Documents, Collections, Users, Roles, Ratings, Bookmarks, Issues) in Postgres
- [ ] Add migrations/setup scripts and apply to dev/prod (optional; using AutoCreate for now)
- [x] Wire Koan.Data.Postgres adapter via options; verify connections
- [x] Switch Development to Postgres (single database across environments)
- [ ] Keep controllers and services (no CQRS); use first-class model statics against Postgres

## Phase 2 – AuthN/Z (policies + ownership)
- [x] Dev: Koan.Web.Auth.TestProvider; Prod: Koan.Web.Auth.Oidc
- [x] Policies: Reader (auth), Author, Moderator, Admin
- [ ] Ownership checks (Author can edit own Draft; Moderator can change any)
- [ ] Remove UI role switcher in non-Development builds

## Phase 3 – Markdown, HTML, sanitization
- [ ] Server-side render MD → sanitized HTML (persist both)
- [ ] Sanitize and normalize tags
- [ ] Prevent script injection in content and summaries

## Phase 4 – Search (FTS + vector)
- [ ] Add tsvector column + GIN index; maintain on upsert
- [ ] Implement FTS query (websearch_to_tsquery)
- [ ] Configure pgvector + ANN index; store embeddings for content
- [ ] Search endpoint supports keyword, semantic, filters, pagination

## Phase 5 – Moderation workflow + events
- [ ] Enforce transitions: Draft→Review (Author), Review→Published/Returned (Moderator), Published→Archived (Moderator/Admin)
- [ ] Emit domain events (Outbox optional): Submitted/Published/Returned
- [ ] Wire messaging (dev Inbox.Http; prod RabbitMQ) and a minimal subscriber

## Phase 6 – AI assistance (real provider)
- [ ] Hook Koan.AI provider for suggestions (title/tags/improvements)
- [ ] Generate quality score with retries/timeouts
- [ ] TOC generation: parse headings server-side; AI fallback when sparse
- [ ] Degrade gracefully when AI unavailable (UI hints)

## Phase 7 – User interactions
- [x] Ratings API: upsert per user; recompute avg/count
- [x] Bookmarks API: add/remove; list user bookmarks
- [x] Issues API: submit; moderator triage status
- [x] View counters: throttled increments or batch (simple increment)
- [ ] Related docs: collection + tag based; optional vector similarity

## Phase 8 – API surface and contracts
- [ ] Stabilize DTOs (list vs detail vs admin)
- [ ] Pagination (FirstPage/Page per DATA-0061), sorting
- [ ] Optional: ETag/If-None-Match for caching
- [ ] Optional: GraphQL for aggregate queries (toc, stats, related)

## Phase 9 – Frontend alignment (remove client mocks)
- [x] Deep-linking: open ?view=<id> on load; update history on mode changes
- [x] Replace localStorage ratings/bookmarks with API calls (optimistic UI)
- [ ] Use IDs from server-rendered HTML for TOC anchors; client only scroll/highlight
- [ ] Tailwind: remove @apply (CDN) or add build step + Typography plugin
- [ ] Hide dev-only controls (role switcher) outside Development

## Phase 10 – Observability, tests, docs
- [ ] Health checks green for DB/AI/MQ in dev
- [ ] Metrics/traces: request/search/AI latencies, moderation events
- [ ] Tests: unit (services), integration (controllers+DB+auth), E2E smoke
- [ ] Update sample README with prod wiring and ops guidance

---

## Acceptance criteria
- [x] Reader cannot fetch draft/review docs via API
- [ ] Search (FTS/vector) returns expected results with filters
- [ ] Moderation transitions persist and emit events
- [ ] Ratings/bookmarks/issues persist and survive refresh
- [ ] AI assistance uses real provider or degrades cleanly
- [ ] Deep-link (?view=id) works; back/forward navigation works
- [ ] Health checks expose DB/AI/MQ and are green in dev

## Notes
- Dev persistence: JSON adapter writes under artifacts/dev-json; safe to delete for a clean slate. Will be replaced by Postgres in Development.
- Prefer first-class model statics for data access in samples/docs: All/Query/FirstPage/Page/Stream
- Keep controllers (no inline endpoints). Centralize constants. One public class per file.
- For production, document decisions under /docs/decisions and update TOCs.
 - Single database: Postgres hosts OLTP, FTS (tsvector), and vectors (pgvector); no separate CQRS store.
