# S5.Recs — “What to watch next?” sample (Mongo + Weaviate + Ollama)

A self-contained sample that demonstrates hybrid recommendations for anime using:
- Document DB (MongoDB) for canonical metadata (titles, genres/tags, episodes, dates, popularity, etc.).
- Vector DB (Weaviate) for “vibes” via embeddings (synopses and curated vibe notes).
- Ollama for local embeddings (auto-detected; falls back to container), powered by Sora.AI.
- Alpine.js front-end served from `wwwroot` with Browse and Admin modes.

This sample showcases Sora’s patterns: controllers for APIs, options for tunables, constants for routes, clean separation of concerns, and an idempotent seed pipeline. It now uses a provider model for data sources so the import path is pluggable and easy to extend.

## What it demonstrates
- Dual storage: facts in MongoDB, vibes in Weaviate.
- Query flow: vector search for candidates → document filter (e.g., slice-of-life, ≤ 24 episodes) → hybrid ranking.
- Personalization loop: 0–5 star ratings update the profile immediately and refresh recommendations.
- Spoiler-safe mode: excludes flagged content from vectors.
- One-click seed: on-page button kicks off a background job to fetch → cache → embed → import.
- Pluggable sources: AniList (online) and a local JSON pack (offline) implement the same provider contract.

## Safety & scope
- SFW-only. Adult/18+ categories and tags are not ingested or shown.
- Public API ingestion is optional; a tiny offline seed pack is included for quick demos.

## Quick start

1) Prerequisites
- Docker Desktop (for MongoDB and Weaviate; optional Ollama container)
- .NET 8 SDK
- Optional: Ollama installed on host (for fastest dev experience)

2) Run the stack
- Use the provided Docker Compose file in this sample (api + mongo + weaviate). If Ollama is not detected on host, the compose service will be used.
- Ports (OPS-0014 compliant, S5 block 5080–5089):
  - API: http://localhost:5084
  - MongoDB: localhost:5081 (Compass)
  - Weaviate: http://localhost:5082
  - Ollama: http://localhost:5083

3) Seed data
- Open the app in a browser. Go to Admin mode and click “Seed dataset.”
- The pipeline will fetch (or load the offline pack), cache normalized items and vectors, and import into Mongo + Weaviate.

4) Try recommendations
- In Browse mode, type a query like: “like Haikyuu!! but cozier and slice-of-life, under 24 eps”.
- Apply filters and rate items; recommendations update instantly.

## Architecture

- API (ASP.NET with Sora):
  - Controllers (attribute-routed):
    - Recommendations: `POST /api/recs/query`, `POST /api/recs/rate`
    - Admin/Seed: `POST /admin/seed/start`, `GET /admin/seed/status/{jobId}`, `GET /admin/seed/sse/{jobId}`
    - Admin/Stats: `GET /admin/stats`
    - Providers discovery: `GET /admin/providers`
  - Services: recommendation engine, embedding/vector indexing, seed orchestrator.
  - Providers: data-source adapters implementing a common contract (see below).
  - Options: embedding provider/model, batch sizes, scoring weights, timeouts.
- Data stores:
  - MongoDB: `AnimeDoc`, `UserProfileDoc`, `RatingDoc` (canonical data + personalization).
  - Weaviate: vectors for `AnimeDoc` (vectorizer=none; cosine; dimensions determined by embedding model).
- Front-end (Alpine.js in `wwwroot`):
  - Browse: query, filters, inline rating (0–5), spoiler-safe.
  - Admin: pick a provider, set a limit (default 1000), start seeding, live status via SSE, dataset stats.
- Seed cache (workspace store on disk): `data/seed-cache/manifest.json`.

## Ollama auto-detection
Resolution order at runtime (cached after first success):
1) http://localhost:11434 (host)
2) http://host.docker.internal:11434 (from container)
3) http://ollama:11434 (compose service)

Model: a small, fast embedding model (e.g., `nomic-embed-text`). On first embed we detect vector dimension and ensure the Weaviate class matches (create if missing; error if mismatch).

If Ollama is unavailable, the app degrades gracefully: vector features are disabled and the API returns popularity/keyword-based results with a status flag.

## Why this design?

- Single Responsibility (SRP): `SeedService` only orchestrates seeding (fetch → embed → import); fetch logic lives in providers (`Providers/*`); vector work is in `EmbedAndIndexAsync`; persistence in repositories.
- Open/Closed (OCP): add a new source by implementing `IAnimeProvider` — no `SeedService` changes. The Admin UI discovers providers at runtime.
- Dependency Injection (DI): providers and services are registered at startup; `Program.cs` scans for `IAnimeProvider` types and adds them to the container.
- Controllers-only routing: all HTTP routes are in controllers (`Controllers/*`), no inline minimal APIs.
- No magic values: stable names and paths in `Infrastructure/Constants.cs`; tunables via typed options (AI/Weaviate).
- Testability: each provider can be unit-tested in isolation; `SeedService` can be tested with fake `IAnimeProvider`, fake AI, and in-memory repos.

## Provider model (IAnimeProvider)

To make the sample an educational reference for clean architecture, the import pipeline is provider-based:

- Contract: each source implements `IAnimeProvider` with the following members:
  - `Code` (short ID, e.g., `local`, `anilist`)
  - `Name` (human label)
  - `Task<List<Anime>> FetchAsync(int limit, CancellationToken ct)`
- Discovery: all implementations are registered automatically at startup (scanning the sample assembly) and exposed via `GET /admin/providers`.
- Current providers:
  - Local JSON (`local`): reads `data/offline/anime.json` for quick offline demos.
  - AniList (`anilist`): paginated GraphQL fetch (SFW: `isAdult:false`), rate-limit backoff, popularity normalization.
- Seed orchestrator uses the requested provider code to fetch, then embeds and imports results.

Add a new provider in 3 steps:
1) Create `Providers/MySourceAnimeProvider.cs` implementing `IAnimeProvider`.
2) Return `Anime` items from `FetchAsync` (set `Id`, `Title`, `Genres`, `Episodes`, `Synopsis`, `Popularity`).
3) Rebuild/run: it will be discovered automatically and appear in the Admin providers list.

Design principles illustrated:
- Separation of concerns: Seed orchestration vs. data access vs. AI embeddings vs. controllers.
- Open/Closed: Adding a source doesn’t change orchestration code.
- DI-first: Replace or mock providers/services in tests.
- No inline endpoints: all HTTP routes live in controllers.
- No magic values: routes/paths/weights centralized in `Infrastructure/Constants` or typed options.

## How to evolve
- Add hybrid retrieval (vector + BM25 keyword) for sharper named entities.
- Plug a cross-encoder re-ranker for the top 50 results.
- Add SSE for real-time seed progress (polling fallback).
- Expand the seed with more content types (trailer transcripts, curated notes); monitor cost and memory.
- Introduce feature flags for scoring weights and explainability toggles.
- Multi-tenant/session isolation via Redis cache for profile vectors.

## Inspect with external tools

- MongoDB (MongoDB Compass)
  - Connection string: `mongodb://localhost:5081`
  - Database: `sora`
  - Notes: Dev-only; no authentication configured in compose. Data persisted under `samples/S5.Recs/data/mongo`.

- Weaviate
  - UI/API: `http://localhost:5082`
  - Useful endpoints: `/v1/schema` (schema), `/v1/graphql` (queries)
  - Notes: Anonymous access enabled in compose for development. Data persisted under `samples/S5.Recs/data/weaviate`.

- Ollama
  - Endpoint: `http://localhost:5083`
  - Examples: `GET /api/tags` (models), `POST /api/embeddings` (embeddings)
  - Notes: If you run Ollama on the host (default 11434), the app will auto-detect it; otherwise the compose service on 5083 is used. Models cached under `samples/S5.Recs/data/ollama-models`.

## Folder layout (expected)
- Controllers/: API endpoints (no inline minimal APIs)
- Services/: rec engine, seed orchestrator, embedding client
- Providers/: source adapters (`local`, `anilist`, …)
- Repositories/: Mongo and Weaviate access
- Models/: contracts and data models
- Options/: typed options (AI, Mongo, Weaviate, scoring)
- Infrastructure/: seed cache I/O, diagnostics, health checks
- wwwroot/: Alpine.js app (browse + admin)
- docker/: compose files and env examples
- data/: host-mounted persistent data (gitignored). Back up this folder to preserve state.
  - seed-cache/: first-level store used by the seeding pipeline
  - mongo/: MongoDB dbpath
  - weaviate/: Weaviate persistence store
  - ollama-models/: Ollama model cache (if running the container)

## Notes
- Respect third-party API ToS; use SFW filters at source (e.g., AniList `isAdult=false`).
- This is a sample; production deployments should add authentication, rate-limits, and observability.
 - Follow Sora’s engineering guidelines (controllers only, centralized constants/options, no empty stubs, one public type per file).
