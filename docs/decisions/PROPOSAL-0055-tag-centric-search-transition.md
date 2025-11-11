# Proposal: Transition from Category-Based Search to Tag-Centric Semantic Retrieval

**Parent Decision:** [ADR-0055: Tag-Centric Semantic Search Rebuild](ADR-0055-tag-centric-semantic-search.md)  
**Date:** 2025-11-09  
**Authors:** Koan Context DX & Architecture Team  
**Scope:** Koan.Service.KoanContext (services, UI, MCP integration), Koan.Data.Vector integration, documentation

---

## Summary

KoanContext currently relies on `SearchCategory`/`SearchAudience` entities and per-chunk `Category` strings to drive semantic search filters and ranking. ADR-0055 mandates a complete rebuild: rip out category/audience infrastructure, introduce tag-first metadata, and ensure both web and MCP channels benefit from richer, explainable retrieval.

This proposal catalogues the codebase changes required to realize ADR-0055, identifying the current surface, the desired end state, and workstreams to cross the gap. The plan assumes a break-and-rebuild posture—no compatibility shims or incremental migrations.

---

## Current Landscape Snapshot

- `Chunk.Category` is set by `Indexer.DetermineCategoryAsync` via cached `SearchCategory` glob patterns.
- `SearchCategory` and `SearchAudience` entities expose CRUD APIs (`SearchCategoryController`, `SearchAudienceController`), UI admin pages, seed logic, and MCP SDK bindings.
- KoanContext search service (`Search.cs`) applies category filters and audience-specific alpha/tokens; ranking ignores richer metadata.
- React SPA modules (compiled into `wwwroot/assets/...`) power “Search Profiles” management and category/audience selectors in the UI.
- MCP surface exposes `/searchcategories` and `/searchaudiences`; request payloads do not permit tag hints or persona overrides.
- ADR-0054 documents the category/audience approach now superseded.

---

## Target Architecture Highlights (ADR-0055)

- `Chunk` and `IndexedFile` share a serialized `TagEnvelope` aggregate (primary, secondary, file tags, frontmatter, audit trail); legacy `Category` state is removed.
- Resolver engine evaluates rule-driven tag pipelines across metadata/frontmatter/content scopes, normalizes via `TagVocabulary`, lowercases tokens at ingestion, and persists audit trails.
- Search requests use a unified `SearchRequestContext`; ranking blends vector similarity, tag match scoring, persona boosts, and recency weights.
- MCP clients provide only query + optional path: unresolved paths trigger multi-project search fallback. The web UI supports richer filters (tag include/exclude/boost, personas).
- Governance UI manages vocabularies, pipelines, synonyms, and monitors rule health.
- Observability tracks rule hit rates, tag entropy, AI fallback usage, and MCP fallback frequency.

---

## Workstream Breakdown

### 1. Legacy Surface Cleanup

- Delete `SearchCategory`/`SearchAudience` models, controllers, seed routines, and cache usage.
- Remove `Chunk.Category` field and associated inference logic.
- Drop SPA modules and API clients tied to category/audience management; regenerate `wwwroot` assets.
- Purge MCP SDK bindings referencing categories/audiences; update client documentation.

### 2. Data Model & Storage Foundation

- Replace per-field tag JSON columns with a shared `TagEnvelope` aggregate on `Chunk` and `IndexedFile`.
- Introduce new records: `TagRule`, `TagPipeline`, `TagVocabulary`, `SearchPersona` (likely in `Models` namespace).
- Determine persistence strategy (partitioned tables for tag rules per project vs global config) and seeding approach.

### 3. Resolver Engine

- Implement scope-specific matchers (path glob, frontmatter parser, content regex/classifier).
- Normalize tags using vocabulary + synonyms; deduplicate by highest confidence per scope.
- Cap emitted tags (e.g., max 6 primary, 10 secondary); write audit trails for governance.
- Optional AI fallback behind feature flag when heuristics emit few tags.

### 4. Indexing Pipeline Integration

- Replace `DetermineCategoryAsync` in `Indexer` with resolver pipeline invocation.
- Ensure tags/audits saved before vector sync operations; chunk persistence updated accordingly.
- Add instrumentation for resolver execution (rule hit counters, warning logging for misconfigurations).

### 5. Search Service Rewrite

- Define `SearchRequestContext`; refactor `/api/search` controller, search service, and MCP handler to use it.
- Implement new ranking formula combining vector similarity, tag TF-IDF/bitmask scoring, persona boosts, and recency.
- Support tag filters (Any/All/Exclude), boosts, persona selection, and multi-project fallback for MCP.
- Ensure query embeddings remain cached; avoid re-embedding when only metadata changes.

### 6. UI/UX Refresh

- Rebuild search UI: tag chips, persona picker, tag provenance modal, minimal top-tag display per result.
- Create governance view for tag vocabulary, synonym management, rule status, and telemetry snapshots.
- Remove legacy category/audience admin surfaces; update frontend API clients to new endpoints.

### 7. SDK & Integration Updates

- Regenerate MCP SDK (`mcp-sdk/koan-code-mode.d.ts`, etc.) with tag governance/search contracts; document request/response changes.
- Adjust any internal consumers that relied on category/audience endpoints or fields.

### 8. Observability & Testing

- Emit metrics for rule hit rates, tag entropy, MCP fallback counts, search latency per channel/persona.
- Add structured logging for tag audit trails and resolver errors.
- Expand unit/integration tests covering resolver accuracy, ranking behaviour, multi-project fallback, and UI interactions.

### 9. Documentation

- Supersede ADR-0054 references, link ADR-0055 in affected docs (guides, API reference, samples).
- Publish developer guidance on tag pipeline configuration and search request construction.

---

## Default Tag Seeds (2025-11-11)

- **Vocabulary** – Canonical entries for `docs`, `adr`, `api`, `guide`, `sample` with normalized synonyms to absorb legacy naming drift (e.g., `documentation`, `how-to`).
- **Rules** – Deterministic rules mapping key surfaces (`docs/**`, `docs/decisions/**`, `docs/api/**`, `docs/guides/**`, `samples/**`) to the seeded tags with tuned confidence/priority tiers.
- **Pipeline** – Default pipeline (`tag-pipeline::default`) wiring all seeded rules, clamping primary tags to 6 and secondary to 10 per ADR guidance, AI fallback disabled by default.
- **Personas** – Baseline personas (`general`, `api-first`, `architecture`) with channel-appropriate boosts and default tag filters for immediate consumption by web and MCP clients.

## Use Cases Enabled

1. **Governance QA sweeps** – The Tag Governance page can surface `adr` coverage gaps and pull chunk samples without bespoke configuration.
2. **API-first discovery** – API-focused teams can select the `api-first` persona to bias results toward endpoint docs and runnable samples on day one.
3. **Architecture compliance reviews** – Reviewers can query the default pipeline for `adr`-tagged decisions to validate implementation alignment during release checkpoints.

## Outstanding Decisions / Risks

- **Storage Layout for Tag Rules:** Decide between global tables vs per-project partitions; affects seeding and governance UI scope.
- **Persona Exposure:** Whether personas remain purely server-side configs or have limited CRUD for admin users.
- **AI Tagging Rollout:** Clarify budget and latency tolerances before enabling optional AI enrichment.
- **Performance Targets:** Benchmark bitmap vs JSON query strategies once tag vocabulary is seeded.

---

## TODO Tracker

- [ ] Remove legacy category/audience code paths (models, controllers, caches, React admin modules, MCP bindings).
- [ ] Redesign `Chunk` and `IndexedFile` storage contracts around `TagEnvelope` aggregates (primary/secondary/file tags + audit + frontmatter).
- [x] Introduce tag governance entities (`TagRule`, `TagPipeline`, `TagVocabulary`, `SearchPersona`) plus seeding strategy. *(Completed 2025-11-11 via `TagSeedInitializer` seeds for default vocabulary, pipeline, and personas.)*
- [ ] Build resolver engine (scope matchers, normalization, deduplication, audit logging).
- [ ] Integrate resolver into indexing flow; delete `DetermineCategoryAsync`; ensure chunk persistence writes tag data.
- [ ] Implement `SearchRequestContext` and refactor `/api/search` logic (web + MCP) to use tag-aware ranking and multi-project fallback.
- [ ] Replace front-end search UI with tag-centric experience; build new governance surface; drop old SPA assets.
- [ ] Update MCP SDK/types and any downstream integrators to the new contract.
- [ ] Add telemetry (metrics, logs, alerts) for tag pipelines and search performance; expand automated tests.
- [ ] Refresh documentation: supersede ADR-0054, publish new guides/samples aligned with ADR-0055.

---

This proposal is living documentation for the ADR-0055 execution effort. Update the TODO tracker and add implementation notes as work progresses.
