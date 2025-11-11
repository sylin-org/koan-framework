# ADR-0055: Tag-Centric Semantic Search Rebuild

**Status:** Accepted  
**Date:** 2025-11-09  
**Context:** KoanContext search surface rebuild for tag-first retrieval  
**Decision Makers:** Koan architecture and DX leads  
**Affected Components:** Koan.Service.KoanContext (services, UI, MCP endpoints), Koan.Data.Vector, docs/search ADR set  
**Supersedes:** ADR-0054 (Entity-Backed Search Profile Management)

---

| **Contract**         | **Details**                                                                                                                                                                                                                                                               |
| -------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Inputs**           | Project manifest data, extracted file content (including frontmatter), resolver rule catalog, canonical tag vocabulary, search request context (web or MCP).                                                                                                              |
| **Outputs**          | Tagged `Chunk` and `IndexedFile` records, serialized tag audit trails, search responses ranked by combined vector/tag scoring, governance telemetry.                                                                                                                      |
| **Error Modes**      | Resolver misconfiguration → indexing failure with surfaced rule id; unknown path/project for MCP → multi-project fallback; tag catalog mismatch → rule suppressed and warning logged; vector provider backpressure → standard retry/backoff already defined in DATA-0085. |
| **Success Criteria** | Consistent tag availability across channels, explicit removal of category-based models, <200 ms overhead for MCP requests, explainable tag provenance per chunk, zero manual migrations (clean rebuild).                                                                  |

---

## Context and Problem Statement

The previous search stack (ADR-0054) depended on `SearchCategory`/`SearchAudience` entities, path glob heuristics, and a single `Category` string per `Chunk`. That approach no longer meets Koan's requirements:

- Hard categories collapse multi-dimensional documents into a single label and hinder hybrid ranking.
- Entity-backed profile CRUD adds DX friction and dual-write overhead without delivering modern discoverability patterns.
- MCP clients can only submit raw queries plus optional paths; they cannot benefit from the heavier web UI affordances.
- Retrofitting tags into the existing model incurs constant migrations and forces re-embedding whenever metadata changes.

Given Koan's greenfield posture for context services, we will rip out the legacy category/audience surface entirely and stand up a tag-centric search experience designed for both rich web queries and constrained MCP usage.

## Decision Drivers

1. **Simplicity over accretion** – Fresh architecture beats incremental patching on stale entities.
2. **Explainable retrieval** – Tags must carry provenance and confidence, not opaque boosts.
3. **Channel duality** – Web surface can be rich; MCP must stay low-latency with minimal inputs.
4. **Operational efficiency** – Tag updates must avoid vector re-embedding.
5. **DX-first governance** – Resolver rules and vocabularies are data, not code branches.

## Decision

We will rebuild KoanContext search around explicit tag resolution pipelines, remove all category/audience entities, and standardize search execution on a unified request context that gracefully adapts to web and MCP channels.

### Key Outcomes

- Delete `SearchCategory`, `SearchAudience`, associated controllers, UI flows, caches, and the `Chunk.Category` property.
- Introduce tag-bearing models for `Chunk`, `IndexedFile`, resolver rules, vocabularies, and personas.
- Execute tagging via a deterministic pipeline (metadata → frontmatter → content) with deduplication and confidence normalization.
- Combine vector similarity, tag scoring, and recency in ranking, with weights sourced from personas or request hints.
- Support two request channels: web (rich filters/boosts) and MCP (path+query only, automatic multi-project fallback when path resolution fails).

## Architecture Overview

### Model Set

- `Chunk`
  - Fields: `Tags` (`TagEnvelope` projecting primary, secondary, file, frontmatter, audit trails), `SearchText`, provenance metadata.
  - Computed view: `AllTagsView` (union of primary/secondary with confidence metadata for query push-down).
- `IndexedFile`
  - Fields: `Tags` (`TagEnvelope` reusing the same shape), `ContentHash`, `FileSize`.
- `TagRule`
  - Immutable resolver definition (`Scope`, `Matcher`, `Emits`, `Confidence`, `Enabled`). Stored as static seed data with optional per-project extensions.
- `TagPipeline`
  - Ordered list of `TagRule` ids per project plus optional `AiFallbackConfig`. Evaluated during indexing.
- `TagVocabulary`
  - Canonical tags with synonyms, display metadata, and guardrails (max cardinality per scope, default confidence weight).
- `SearchPersona`
  - Lightweight configuration for weights (`w_semantic`, `w_tags`, `w_recency`), token budgets, tag boosts. Referenced by request context but not an entity requiring CRUD UI.

### Tag Resolution Pipeline

1. **Metadata Scope** – Path segment heuristics (`samples/`, `src/`, etc.) applied via glob rules.
2. **Frontmatter Scope** – Parse YAML/JSON frontmatter and honor explicit tag overrides.
3. **Content Scope** – Chunk-level detectors (regex, language classifiers, heading inference).
4. **Normalization & Deduplication** – Map synonyms to canonical tags, select highest confidence per tag, clamp to configured limits (max 6 primary, 10 secondary).
5. **Audit Trail** – Persist rule id, scope, and confidence for every emitted tag; expose in governance UI and optional debug output.

AI enrichment is optional and guarded by feature flags; it only executes when heuristics emit fewer than two tags and the file is below the configured size threshold.

### Search Request Context

A shared `SearchRequestContext` object (used by both web controllers and MCP handlers) carries:

- `ProjectIds` and/or `PathContext`
- `TagsAny`, `TagsAll`, `TagsExclude`
- `TagBoosts`
- `PersonaId`
- `Channel` (`Web` or `Mcp`)
- `ContinuationToken`

For MCP calls:

- Attempt project resolution via `PathContext`.
- On failure, execute multi-project search (all indexed projects) with token-budget-aware pagination.
- Skip expensive insight generation and AI tag assists to minimize latency.

### Ranking Formula

```
score = (vectorSimilarity * w_semantic)
      + (tagMatchScore * w_tags)
      + (personaBoost * w_persona)
      + (recencyScore * w_recency)
```

- `tagMatchScore` uses lightweight TF-IDF over primary tags with bitmap acceleration for top-N tags; fall back to the `AllTags` projection for rare tags.
- `personaBoost` applies configured boosts/penalties from `SearchPersona` when present; MCP defaults to the "general" persona unless explicitly provided.
- Weights come from persona defaults or request overrides, clamped to `[0,1]` and normalized to 1.0.

### Storage & Querying

- Persist `TagEnvelope` as the source of truth so models interact with typed, normalized collections.
- Project curated primary tags into a 128-bit bitmap column for hot-path filters (up to 128 curated tags) while retaining the envelope for full fidelity.
- Keep secondary and file/frontmatter tags inside the envelope for long-tail analytics and governance review.
- Expose a generated column (`AllTags`) that un-nests the envelope for analytics queries and fallback filters.
- Vector store metadata remains unchanged; embeddings reference chunk id only, so tag updates do not force re-embedding.

### UI/UX Implications

- **Search Web UI** – Persona picker, tag auto-complete chips, explicit include/exclude controls, and optional tag provenance modal per result. Default view shows top three primary tags per chunk.
- **Governance UI** – Manage vocabulary, synonyms, rule status, and monitor tag distribution/entropy metrics. Editing resolvers updates data records, not code.
- **MCP Responses** – Minimal payload (text, score, top tags, source URI, optional audit snippet) to keep clients light.

### Observability & Operations

- Metrics: rule hit rate, tag entropy per project, MCP vs web latency, AI fallback usage, multi-project fallback frequency.
- Logs: structured entries for rule emissions (`ruleId`, `scope`, `confidence`, `chunkId`).
- Alerts: high failure count in resolver pipeline, sustained increase in multi-project searches (indicating path resolution drift).
- Nightly governance job: detect orphaned tags (not in vocabulary) and low-signal rules (<5% hit rate).

## Edge Cases & Mitigations

1. **Missing Project for MCP Query** – Automatically invoke multi-project search, return aggregated results with explicit warning flag.
2. **Resolver Explosion** – Guard with per-scope tag limits and entropy monitoring; block pipeline activation if rule set would exceed thresholds.
3. **Synonym Collision** – Vocabulary loader rejects conflicting canonical mappings and surfaces actionable errors.
4. **Rule Misconfiguration** – Indexing halts with explicit `TagRuleFailure` containing rule id and offending matcher; requires fix before retry.
5. **Vector Provider Backpressure** – Leverage existing DATA-0085 retry policy; tagging pipeline unaffected because tags are stored before vector sync.

## Implementation Plan

1. **Purge Legacy Surface** – Delete category/audience entities, controllers, caches, UI pages, and `Chunk.Category` references. Update code to remove inference paths referencing ADR-0054.
2. **Model Introductions** – Add the shared `TagEnvelope` field to `Chunk` and `IndexedFile`; scaffold `TagRule`, `TagPipeline`, `TagVocabulary`, `SearchPersona` records.
3. **Resolver Engine** – Implement pipeline executor with scope-aware matchers, normalization, deduplication, and audit trail persistence.
4. **Indexing Integration** – Wire resolver engine into `Indexer`, ensuring tag writes happen before chunk persistence and vector queuing.
5. **Search Service Rewrite** – Accept `SearchRequestContext`, implement tag-aware ranking/filters, add multi-project fallback path for MCP.
6. **UX Replacements** – Build new governance view and refresh web search interface to consume tag metadata; remove deprecated category UI.
7. **Instrumentation** – Emit metrics/logs, configure dashboards, and set alert thresholds.
8. **Docs & Samples** – Update developer guides, API reference, and sample code to showcase tag-centric queries. Mark ADR-0054 as superseded by this ADR.

## Consequences

- **Positive**

  - Tags become first-class signals without re-embedding costs.
  - Web and MCP channels share core logic while honoring channel constraints.
  - Governance is data-driven and explainable.

- **Negative**

  - Requires full rebuild of existing admin UI and API contracts.
  - Downstream consumers must adopt new request contract; no category compatibility layer will ship.
  - Additional storage for tag audit metadata (acceptable given clarity gains).

- **Neutral/Follow-up**
  - Performance tuning for bitmap vs `AllTags` scans will need benchmarking once seed vocabularies are defined.
  - AI-assisted tagging remains optional; adoption depends on future budget and latency targets.

## References

- DATA-0054 – Vector search capability and contracts (ranking integration).
- DATA-0061 – Paging and streaming guidance (applies to multi-project fallback).
- ARCH-0063 – Docs IA realignment (governance UI alignment).
- ADR-0050, ADR-0051, ADR-0053 – Previous search optimizations for pagination and hybrid scoring.
