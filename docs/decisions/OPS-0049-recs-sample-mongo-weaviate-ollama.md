---
title: OPS-0049 — Recommendations Sample (Mongo + Weaviate + Ollama) with Alpine.js UI and Seed Pipeline
status: Accepted
date: 2025-08-20
---

## Context

We want a demonstrable sample that showcases a dual-database pattern:
- Document DB for canonical facts (titles, genres/tags, episodes, dates, platforms, popularity, etc.).
- Vector DB for “vibes” and semantics (synopses, curated vibe tags/notes), enabling semantic search and hybrid ranking.

User experience:
- Query like: “like Haikyuu!! but cozier and slice‑of‑life, under 24 eps”.
- Vector search produces candidates by vibe; document DB applies hard filters (genre, episode count); hybrid score (semantic + popularity + tag overlap) ranks results.
- Users rate items (0–5). Each rating immediately updates the user profile and re-computes recommendations.
- Spoiler‑safe mode excludes flagged content from vector neighborhoods.

Operational UX:
- On-page “Seed dataset” button starts a background seeding job.
- Load process: Fetch → Persist to workspace seed cache → Import into Mongo + Weaviate → App ready.
- SFW-only ingestion and UI (exclude adult categories entirely).

## Decision

1) Storage choices
- Document DB: MongoDB (Koan.Data.Mongo) for flexible metadata.
- Vector DB: Weaviate (Koan.Data.Weaviate) with vectorizer = none. The app supplies embeddings.

2) Embeddings
- Provider: Ollama by default. Auto-detect endpoints in this order and cache the first healthy:
  - http://localhost:11434 (host)
  - http://host.docker.internal:11434 (from container to host)
  - http://ollama:11434 (compose service)
- Model: a small, fast embedding model (e.g., nomic-embed-text). On first embed, detect vector dimension and validate/create Weaviate schema accordingly.

3) UI layer
- Alpine.js app served from wwwroot (no SPA build chain). Two modes:
  - Browse: query, filters, anchor picker, results, inline 0–5 rating, live refresh.
  - Admin: seed controls (source, limit, overwrite), progress, stats/diagnostics.

4) Seed pipeline
- Fetch from public APIs (optional; e.g., AniList GraphQL with isAdult=false) or local seed pack.
- Persist to a first-level seed cache on disk (raw, normalized, vectors, manifest) with deterministic IDs and content hashes for idempotency and re-use.
- Embed texts in batches with retry/backoff; reuse cached vectors by content hash.
- Import via upsert (skip unchanged). Overwrite option purges orphans.

5) API surface (controller-routed)
- POST /api/recs/query — hybrid recommendations.
- POST /api/recs/rate — store rating; update profile; return refreshed recs.
- GET /api/anime/search — anchor/typeahead (SFW only).
- POST /admin/seed/start; GET /admin/seed/status/{jobId}; GET /admin/stats — admin only.

6) Safety & policy
- SFW-only taxonomy and ingestion (deny/strip adult content at source and app levels).
- Spoiler‑safe mode: exclude flagged content; conservative heuristics.
- Admin endpoints require auth and rate limiting.

7) Runtime & ops
- Ship Docker Compose: api + mongo + weaviate (+ optional ollama). Volumes: mongo_data, weaviate_data, seed_cache.
- Degrade gracefully if embeddings unavailable: keep keyword/popularity recommendations and surface a status flag.

## Consequences

Positive
- Clear demonstration of Koan’s data and AI modules working together.
- One-click seeding with caching makes demos reproducible and fast after first run.
- Alpine.js keeps the front-end simple and server-agnostic.

Negative / Risks
- First-time model pull and warm-up can be slow; must communicate clearly in the admin UI.
- Schema/vector dimension mismatch will block import; requires upfront validation.
- Public API rate limits can slow initial fetch; cache and small limits are essential.

## Alternatives considered
- Postgres + pgvector instead of Weaviate: simpler ops, but weaker schema/faceting for this demo.
- Weaviate built-in vectorizers: convenient but less consistent across environments; we prefer app-supplied vectors.
- React/Vue SPA: richer UI but higher complexity; Alpine.js is sufficient.
- No seed cache: simpler, but re-seeding would be slow and costly (embeddings repeated).

## Scope & non-goals
- In scope: A sample app under samples/S5.Recs, controllers, services, options, wwwroot UI, seed pipeline, compose.
- Out of scope: Advanced re-ranking models, user auth flows beyond admin API key, NSFW support.

## Implementation notes
- Vector dim detection on first successful embed; create/validate Weaviate class (ContentPiece) with that dim.
- Deterministic IDs: animeId = sourcePrefix:sourceId; contentId = sha256(animeId + type + normalizedTextHash).
- Profile vector: centroid(positives) − centroid(negatives) + tag weights; normalize.
- Scoring: 0.6 semantic + 0.2 popularity + 0.15 tag overlap + 0.05 novelty/diversity.

## Rollout
- Add sample at samples/S5.Recs with README, endpoints, minimal offline seed pack.
- Provide compose file and environment examples.
- Add docs/guides page referencing this ADR.

## Success criteria
- End-to-end demo: seed → browse → rate → refreshed recs within seconds.
- Idempotent reseed with high cache hit rate.
- Admin diagnostics show selected Ollama endpoint and model, counts, and last run summary.
