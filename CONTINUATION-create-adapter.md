# Continuation notes — `koan-create-adapter` skill + four proven adapters

Task brief: `agent-prompts/koan-create-adapter-skill.md` (read it first). This file is the resume
point. Phases 1–2 and the document seam of Phase 3 are complete, committed, and green at the
boundary; the vector and AI seams of Phase 3 remain.

## Done

- **Commit 650c67581** — Phase 1 (skill) + Phase 2 (Firebird relational adapter).
- **Commit (see `git log`)** — Phase 3 document seam: `references/document.md` + CouchDB adapter.
- **Skill**: `.agents/skills/koan-create-adapter/` (SKILL.md, references/data.md,
  references/document.md, agents/openai.yaml); `scripts/skills-verify.ps1` expected-skills list
  carries the fourth entry; `docs/guides/agent-skills.md` chooser table has the pointer row.
- **Firebird** (`Sylin.Koan.Data.Connector.Firebird`): 14/14 specs green ×2 against
  `firebirdsql/firebird:5.0.4` (AODB record-plane oracle incl. isolation modes, streaming
  fail-closed, polymorphic roots; full filter-convergence corpus; scalar pushdown guard;
  residual-fact honesty; scalar ordering; paged windows; capability truth; boot provenance).
- **CouchDB** (`Sylin.Koan.Data.Connector.CouchDb`): 12/12 specs green ×2 against `couchdb:3.5`
  (AODB record-plane oracle; full filter corpus WITH strict pushdown guard, `$like` posture pinned
  residual-and-recorded; paged windows through declared sort fallback; capability truth; boot
  provenance). Pure HttpClient — no driver.
- Both pack verified with `Sylin.` ids and release-train `version.json`. Both ship **not assessed**.

## Remaining (Phase 3, in brief order)

### 1. Vector seam → Chroma (NOT STARTED)

Findings already gathered (save the next session the recon):

- Oracle: `tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.TestKit/`
  — `VectorAodbConformanceSpecsBase` (isolation modes + the V-01..V-24 annex via
  `ProveVectorAnnexCellAsync`), plus `VectorFilterConvergenceSpecsBase`, `VectorPartitionSpecsBase`,
  `EmbeddingFactory`, `TodoVector`, `VectorAdapterTestServices`, `IVectorAdapterTestFactory`.
- Exemplar: **Qdrant** (`src/Connectors/Data/Vector/Qdrant/` — client/filter/repository split,
  1400 lines total) and its test class
  `Koan.Data.VectorAdapterSurface.Qdrant.Tests/QdrantVectorAodbConformanceSpec.cs`, which
  implements the ~24 annex proof methods and DECLINES the earned-but-unclaimed cells
  (V-12 eventual, V-14 hybrid, V-15 named spaces, V-16 continuation, V-18 atomic batch, V-19
  export) — a conformant adapter declines with reasons. SqliteVec is the in-process exemplar.
- Chroma mapping: REST v2 (`/api/v2`), collection = Koan vector container; create collection
  (cosine/l2/ip) under managed lifecycle; `POST /collections/{name}/upsert` (ids, embeddings,
  metadatas, documents); `POST /query` (query_embeddings, n_results, where, include); `GET`/`DELETE`
  by ids; metadata `where` dict for filter pushdown (V-13). Testcontainers `chromadb/chroma` (image
  pulls in seconds; no HEALTHCHECK trouble observed historically — pin a wait on `/api/v2/heartbeat`).
- Vector plane peculiarity: the Database mode is a NAME-FOLD floor on HTTP adapters (routed source →
  distinct collection name), no fail-closed throw — see the kit's class comment.
- Write `references/vector.md` FIRST from Qdrant + the kit, fix it while building (same dogfood loop
  as data/document).

### 2. AI seam → llama.cpp (NOT STARTED)

- No shared AI conformance kit exists (ARCH-0127 — a missing kit is a STOP for kit-building, not for
  the adapter). Construct the strongest behavioral proof from the exemplar's test shape.
- Exemplar: LMStudio or Ollama connector under `src/Connectors/AI/`. llama-server exposes OpenAI-
  style `/v1/chat/completions`, `/v1/embeddings` plus native `/completion`, `/health`. Wire-contract
  fake server over a real HTTP listener if a real model is impractical; say so in the report.
- Write `references/ai.md` from the exemplar, fix while building.

### 3. Obligations sweep (what remains after vector/AI)

- Capability map / connector matrix: NOT owed rows for not-assessed packages (skills-verify only
  requires rows for assessed pieces); the matrix is generated — note for regeneration (run
  `scripts/build-connector-matrix.ps1` at a boundary; pre-existing shelf failure
  `Sylin.Koan.Data.Hygiene` is NOT this task's and predates it).
- AOT publish attempt (DONE, FIXED 2026-08-29): the `PublishAot=true` win-x64 consumer over
  Firebird+CouchDb published clean and died at startup on `HealthProbeScheduler` ("no suitable
  constructor" under ILC). FIXED at the seam: `[param:/property: DynamicallyAccessedMembers(
  PublicConstructors)]` on `KoanRegistry`'s `BackgroundServiceDescriptor.ServiceType` and
  `ServiceDiscoveryAdapterDescriptor.ServiceType` — the Types reach DI by reflection through the
  generated registry, so their ctors needed rooting. Probe binary now boots and serves HTTP 200;
  Koan.Tests.Core.Unit 119/119 green. Remaining AOT surface (out of scope, recorded): trim warnings
  IL2026/IL3050 in `KoanLockfileSerializer` (System.Text.Json reflection), and FirebirdClient's
  wire-level AOT behavior past boot is still unverified.
- `MakeGenericType` sweep: done per seam (clean); re-run after vector/AI.

### 4. Final report

Phase-by-phase: what shipped, oracle numbers, playbook fixes, selection rationale, deviations.

## Run-book facts

- Suites run best DIRECTLY via the xunit v3 exe (live output):
  `/tmp/Koan-framework/tests/<TestProject>/bin/Debug/net10.0/<name>.exe -noColor`
  (repo builds into the shared temp output root, NOT the working tree).
- `MSYS_NO_PATHCONV=1` required for docker env vars with absolute paths (Git Bash mangles them).
- Firebird container env: WireCrypt=Enabled, AuthServer="Srp256, Srp", FIREBIRD_ROOT_PASSWORD (not
  ISC_PASSWORD); image has NO healthcheck — fixture waits on the internal port.
- One build at a time (shared output path). Stack a stalled test process with
  `dotnet-stack report -p <pid>` before believing any "flaky" claim.
