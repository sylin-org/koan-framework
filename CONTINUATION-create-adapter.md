# Continuation notes — `koan-create-adapter` skill + four proven adapters

Task brief: `agent-prompts/koan-create-adapter-skill.md` (read it first). This file is the resume
point; Phases 1–2 are complete, committed, and green at the boundary.

## Done (commit 650c67581 on dev)

- **Phase 1 — skill**: `.agents/skills/koan-create-adapter/` (SKILL.md, references/data.md,
  agents/openai.yaml). `scripts/skills-verify.ps1` `$expectedSkills` gained the fourth entry. The
  data playbook was corrected against current code while dogfooding (document-column mapping,
  shadow columns, oracle hosting truth, substrate seam inventory).
- **Phase 2 — Firebird relational adapter**: `src/Connectors/Data/Firebird/` +
  `tests/Suites/Data/Connector.Firebird/` (+ Directory.Packages.props, Koan.sln). **14/14 specs
  green twice** against real `firebirdsql/firebird:5.0.4` via Testcontainers: AODB record-plane
  conformance, full filter-convergence corpus, scalar pushdown guard, residual-fact honesty, scalar
  ordering, paged windows, capability truth, boot provenance. Pack verified
  (`Sylin.Koan.Data.Connector.Firebird`). Ships **not assessed**.
- Key design fact for reviewers: the default relational mapping is `(Id, Json)`; the Firebird
  dialect has no JSON functions, so top-level scalars + managed discriminators mirror into shadow
  columns (dialect `Read` → quoted column; `plan.ShadowValues(entity)` on writes; DDL creates them).
- Container auth facts (would break any re-run): WireCrypt=Enabled, AuthServer="Srp256, Srp",
  FIREBIRD_ROOT_PASSWORD (not ISC_PASSWORD), port-based wait (no HEALTHCHECK in the image).
- Run the suite directly for live output: the built exe under the shared temp output root
  (`/tmp/Koan-framework/tests/Koan.Data.Connector.Firebird.Tests/bin/Debug/net10.0/`), `MSYS_NO_PATHCONV=1`
  (Firebird paths in env vars get mangled otherwise).

## Remaining (Phase 3, in brief order)

1. **Document seam → CouchDB** (pure HttpClient, no driver; Testcontainers `couchdb:3`). Exemplar:
   Mongo connector + its fixture/AODB hosting. Write `references/document.md` first from the
   exemplar, fix it while building.
2. **Vector seam → Chroma** (HttpClient; Testcontainers `chromadb/chroma`). Exemplar: SqliteVec or
   Qdrant. Oracle: `tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.TestKit/
   VectorAodbConformanceSpecsBase.cs`. Write `references/vector.md`.
3. **AI seam → llama.cpp (`llama-server`)** — local runtime, NOT hosted (ARCH-0127 gates hosted AI
   only). Exemplar: Ollama or LMStudio connector. No shared AI kit (ARCH-0127) — construct the
   strongest behavioral proof from the exemplar's test shape (wire-contract fake server). Write
   `references/ai.md`.
4. **Obligations sweep**: capability-map rows (not-assessed, truthful), `docs/guides/agent-skills.md`
   chooser pointer for the new skill, note `docs/reference/connector-matrix.md` needs regeneration
   (generated; do not hand-edit), recipe ingredients where the adapters belong, AOT publish attempt
   per adapter (report blocker if RID-blocked), MakeGenericType sweep on changed contracts.
5. **Final report** per the brief: phase-by-phase, oracle numbers, playbook fixes, selection
   rationale, deviations.

## Selection rationale (recorded)

- Firebird: free (IPL/MIT), absent from matrix, managed ADO.NET provider, Testcontainers-official
  image, SQL-standard dialect. Lost: libSQL (immature .NET clients), ClickHouse (async-mutation
  model conflicts with delete/upsert outcome semantics), H2 (Java-only).
- CouchDB: Apache-2.0, absent (Couchbase ≠ CouchDB), plain REST/JSON → zero driver dependency,
  Mango queries for filter pushdown. Lost: RavenDB Community (heavy client, license constraints).
- Chroma: Apache-2.0, absent, REST. Lost: LanceDB (prerelease .NET bindings, native deps).
- llama.cpp: MIT, local runtime (same class as shipped Ollama/LMStudio), OpenAI-compatible API.
