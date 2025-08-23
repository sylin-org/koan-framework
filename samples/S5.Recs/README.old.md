# S5.Recs — “What to watch next?” sample (Mongo + optional Vector + Ollama)

An approachable sample that demonstrates hybrid recommendations for anime with a focus on simplicity and good practices:
- MongoDB stores canonical anime metadata and all user-specific library signals.
- Optional vector search (Weaviate) improves relevance when configured.
- Ollama provides local embeddings; the app degrades gracefully when AI/vector are unavailable.
- A static UI under `wwwroot` offers Browse and Admin views.

The sample adheres to Sora guidelines: controllers-only routing, typed options (no magic values), centralized constants, small entities, and clear separation of concerns.

## Design decisions (Aug 2025)

- User-centric ranking store
  - All user signals (Favorite, Watched, Dropped, Rating, AddedAt) live in `LibraryEntryDoc` keyed by `{UserId}:{AnimeId}`.
  - Rating is co-located with status; the old `RatingDoc` is deprecated/removed.
  - When a user rates an item without a status, we auto-set `Watched` for simplicity.
- Lightweight user profiles
  - `UserProfileDoc` maintains genre/tag weights and an optional preference vector via EWMA updates.
  - Preferences consider both Genres and Tags from the user’s library.
- Simple, tunable scoring
  - Base hybrid: Vector (if available) + Popularity + Preference boosts/penalties (centered around neutral).
  - “For You” excludes Watched/Dropped and lightly boosts Favorites and close neighbors.
- “Try Something New” (per-request nudges)
  - Users can pick up to `MaxPreferredTags` tags/genres to softly boost results without taking over the ranking.
  - An optional Diversity bonus can inject novelty when vectors are available.
- Admin-configurable settings
  - `SettingsDoc` (Id=`recs:settings`) persists `PreferTagsWeight`, `MaxPreferredTags`, `DiversityWeight`.
  - Admin page can GET/POST these tunables; Recs reads effective values via options/provider.

## What it demonstrates

- Clean, controller-only APIs with centralized constants and options.
- Provider-based seed pipeline (AniList or local JSON), idempotent and observable.
- Personalization loop that stays readable: status/rating → profile update → better recommendations.
- Graceful fallbacks when AI/Vectors are not configured.

## Quick start

1) Prerequisites
- Docker Desktop (MongoDB; Weaviate optional; Ollama optional)
- .NET 8 SDK

2) Run
- Start the app (and Weaviate/Ollama if desired). The API runs on the S5 block (e.g., http://localhost:5084).

3) Seed data
- Use the Admin page to seed from AniList or the local JSON pack. The pipeline fetches → embeds (if AI available) → imports.

4) Try it
- Browse: search or just use “For You” (with or without vectors). Rate a few items and see personalization kick in.
- Admin: view stats, manage providers, and adjust recommendation settings.

## API surface

- Recommendations (`/api/recs`)
  - `POST /api/recs/query` — returns recommendations. If `userId` is provided and no `text/anchor` is given, a profile-vector path is used (when available). Excludes the user’s Watched/Dropped; lightly boosts Favorites. Supports filters and the “Try Something New” boost.
  - `POST /api/recs/rate` — upserts rating for `{userId, animeId}`; if no status exists, auto-sets `Watched`. Also updates the user profile (genre/tag weights and pref vector when AI is available).
- Library (`/api/library`)
  - `PUT /api/library/{userId}/{animeId}` — body can include `{ favorite?, watched?, dropped?, rating? }`. Enforces `Watched` xor `Dropped`; `Favorite` can coexist. `rating` requires `Watched` or `Dropped`.
  - `DELETE /api/library/{userId}/{animeId}` — resets status and clears rating.
  - `GET /api/library/{userId}?status=&sort=&page=&pageSize=` — returns a paged list joined with anime metadata. Sorts: Relevance, Rank (Popularity), ReleaseDate (if present), AddedAt.
- Users (`/api/users`)
  - `GET /api/users` — returns users (the sample auto-creates a single “Default User” if empty).
  - `POST /api/users` — creates a user.
  - `GET /api/users/{id}/stats` — `{ favorites, watched, dropped }` for the Welcome Back chip.
- Admin (`/admin`)
  - Seed & stats: `POST /admin/seed/start`, `GET /admin/seed/status/{jobId}`, `GET /admin/stats`, `GET /admin/providers`.
  - Vector-only upsert: `POST /admin/seed/vectors`.
  - Recommendation settings: `GET /admin/recs-settings`, `POST /admin/recs-settings` (see Settings below).

All routes are declared in controllers; route segments and defaults live in `Infrastructure/Constants.cs`.

## Data model

- `AnimeDoc` (Mongo; optional vectors)
  - Canonical anime metadata: titles, genres, tags, episodes, synopsis, popularity, media URLs; optional Year/Rank if available.
- `UserDoc` (Mongo)
  - Minimal user record: Id, Name, IsDefault, CreatedAt. The sample seeds a “Default User”.
- `LibraryEntryDoc` (Mongo) — user-centric ranking store
  - Id = `{UserId}:{AnimeId}`, `UserId`, `AnimeId`, `Favorite` (bool), `Watched` (bool), `Dropped` (bool), `Rating` (int? 0..5), `AddedAt`, `UpdatedAt`.
- `UserProfileDoc` (Mongo)
  - `GenreWeights` (includes tags), optional `PrefVector`, `UpdatedAt`.
- `SettingsDoc` (Mongo; Id = `recs:settings`)
  - `PreferTagsWeight` (0..1.0; default 0.2), `MaxPreferredTags` (1..5; default 3), `DiversityWeight` (0..0.2; default 0.1), `UpdatedAt`.

## Scoring and sorting (simple, tunable)

- Base relevance
  - Vector similarity (if available) + Popularity + centered preference term from user’s Genre/Tag weights.
  - Spoiler penalty is applied when SpoilerSafe.
- “Try Something New”
  - Per-request `PreferTags` (up to `MaxPreferredTags`) adds a bounded boost (`PreferTagsWeight`). Diversity bonus (optional) favors items less similar to the profile vector.
- Sort options
  - Relevance (default), Rank (Popularity proxy), Release Date (when present), AddedAt (from library).

## Admin: recommendation settings

- `SettingsDoc` persists tunables; GET/POST via Admin controller. The app clamps values to safe ranges and applies them live.
- Fields
  - `PreferTagsWeight` — how strongly to boost preferred tags (default 0.2).
  - `MaxPreferredTags` — max chips in the “I want to watch a…” selector (default 3).
  - `DiversityWeight` — small novelty bonus when vectors are available (default 0.1).
-
When AI or vectors are disabled/unavailable, related terms are ignored and the app falls back to popularity and user preferences.

## Folder layout

- `Controllers/` — API endpoints (attribute-routed)
- `Services/` — recommendation engine and seeding orchestrator
- `Providers/` — source adapters (`local`, `anilist`, …)
- `Models/` — contracts and data models (`AnimeDoc`, `UserDoc`, `LibraryEntryDoc`, `UserProfileDoc`, `SettingsDoc`, view models)
- `Options/` — typed options (AI model, etc.)
- `Infrastructure/` — constants and utilities
- `wwwroot/` — static UI (browse + admin)
- `data/` — dev-only seed/mongo/vector caches (gitignored)

## Notes

- SFW-only ingestion; adult content is not included.
- Sample-grade runtime: no auth; add rate-limits and authentication for production.
- Keep code small and readable: controllers define routes, constants collect literals, options carry tunables, and entities remain minimal.
