# continuation.md — `koan-create-adapter` skill + proven adapters

Handoff for a fresh session. Read `agent-prompts/koan-create-adapter-skill.md` (the task brief),
then this file. Phase 1, Phase 2, and the document seam of Phase 3 are complete, committed on
`dev`, and green at their boundaries. Remaining: the **vector** and **AI** seams, their playbooks,
and their obligations. Everything needed to finish is below; nothing needs re-discovery.

(The analytics pillar's handoff that previously lived here is preserved, unchanged, as
`continuation-analytics-2026-08-28.md` — that session's work shipped and this file superseded it.)

## Current state (all committed on `dev`)

| Commit | What |
|---|---|
| `650c67581` | Phase 1 skill (`.agents/skills/koan-create-adapter/`: SKILL.md, references/data.md, references/document.md, agents/openai.yaml) + Phase 2 **Firebird** adapter |
| `67c5518ef` | Phase 3 document seam: **CouchDB** adapter + `references/document.md` |
| `508cdaa80` | Framework amendments: AOT ctor rooting on `KoanRegistry` descriptors, generated truth regenerated, MEMORY.md learnings, workbook run-book, `docs/guides/agent-skills.md` chooser row |
| `d58ab2f4d` | Fleet quality sweep — **105/105 structurally-ready, 0 findings** |
| `9e896fd8a` | `KoanLockfileSerializer` on source-generated `KoanLockfileJsonContext` (IL2026/IL3050 gone) |
| `6e9debb57` | AOT surface closed: FirebirdClient verified wire-functional under NativeAOT |

Proven by suites run **twice**, green:

- **Firebird** (`Sylin.Koan.Data.Connector.Firebird`): 14/14 — AODB record-plane oracle
  (isolation modes declared+realized, streaming fail-closed, polymorphic roots), full
  filter-convergence corpus, scalar pushdown guard, residual-fact honesty, scalar ordering, paged
  windows, capability truth, boot provenance. Real `firebirdsql/firebird:5.0.4`.
- **CouchDB** (`Sylin.Koan.Data.Connector.CouchDb`): 12/12 — AODB record-plane oracle, full filter
  corpus WITH strict pushdown guard (`$like` posture pinned residual-and-recorded), paged windows
  through the declared sort fallback, capability truth, boot provenance. Pure HttpClient, no
  driver. Real `couchdb:3.5`.

Both pack verified (`Sylin.` ids, release-train `version.json`), both **not assessed** (⚠ in the
matrix — merging grants nothing, ARCH-0120).

Framework side, closed: NativeAOT single-binary story holds **end-to-end** (consumer over both
adapters boots, serves 200, and FirebirdClient does a full DDL/upsert/select/delete wire
round-trip in the 7.7 MB binary, exit 0). `skills-verify -Structure` passes fully. Package fleet
0 findings.

## Remaining work (in brief order)

### 1. Vector seam → Chroma

Recon already done (do not redo):

- Oracle: `tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.TestKit/` —
  `VectorAodbConformanceSpecsBase` (isolation modes + V-01..V-24 annex via `ProveVectorAnnexCellAsync`),
  `VectorFilterConvergenceSpecsBase`, `VectorPartitionSpecsBase`, `EmbeddingFactory`, `TodoVector`,
  `VectorAdapterTestServices`, `IVectorAdapterTestFactory`.
- Exemplar: **Qdrant** (`src/Connectors/Data/Vector/Qdrant/`, client/filter/repository split,
  ~1400 lines) and its test class
  `Koan.Data.VectorAdapterSurface.Qdrant.Tests/QdrantVectorAodbConformanceSpec.cs`, which
  implements the ~24 annex proof methods and DECLINES earned-but-unclaimed cells (V-12 eventual,
  V-14 hybrid, V-15 named spaces, V-16 continuation, V-18 atomic batch, V-19 export) — declining
  with reasons is conformant. SqliteVec is the in-process exemplar.
- Chroma mapping: REST v2 (`/api/v2`); collection = Koan vector container; create collection
  (cosine/l2/ip) under managed lifecycle; `POST /collections/{name}/upsert` (ids, embeddings,
  metadatas, documents); `POST /query` (query_embeddings, n_results, where, include); get/delete
  by ids; metadata `where` dict for filter pushdown (V-13). Testcontainers image
  `chromadb/chroma`; wait on `/api/v2/heartbeat` (check the image's HEALTHCHECK before trusting
  the default wait — see run-book).
- Vector-plane peculiarity: Database mode is the NAME-FOLD floor on HTTP adapters (routed source →
  distinct collection name, no fail-closed throw) — see the kit's class comment.
- Write `references/vector.md` FIRST from Qdrant + the kit, fix it while building (same dogfood
  loop as data/document). Then implement `src/Connectors/Data/Vector/Chroma/` + test project
  `Koan.Data.VectorAdapterSurface.Chroma.Tests`, modelled on the Qdrant pair.

### 2. AI seam → llama.cpp (`llama-server`)

- No shared AI conformance kit exists (ARCH-0127 — a missing kit is a STOP for kit-building, not
  for the adapter). Construct the strongest behavioral proof from the exemplar's test shape.
- Exemplar: LMStudio or Ollama under `src/Connectors/AI/`. llama-server exposes OpenAI-style
  `/v1/chat/completions`, `/v1/embeddings` plus native `/completion`, `/health`.
- If a real model is impractical, prove the wire contract with a deterministic fake HTTP server
  (the ARCH-0120 "wire-contract service" posture) and say so in the report.
- Write `references/ai.md` from the exemplar, fix while building.

### 3. Obligations (per adapter, after it is green)

- Capability map / matrix: not-assessed packages are **not owed rows**; matrix is generated — run
  the regeneration commands below and commit the ⚠ diff.
- AOT: extend the probe recipe (below) with the new adapter; publish **and run**; record result.
- `MakeGenericType` sweep on changed contracts:
  `grep -rn "MakeGenericType" src tests --include="*.cs"`.
- README must satisfy the quality gate on first write (see run-book) — Firebird/CouchDb needed a
  second pass; don't repeat that.

### 4. Final report

Phase-by-phase: what shipped, oracle numbers, playbook fixes, selection rationale, deviations.

## Selection rationale (recorded, do not re-litigate)

- Firebird: free, absent from matrix, managed ADO.NET provider, official container, SQL-standard
  dialect. Lost: libSQL (immature .NET clients), ClickHouse (async-mutation model conflicts with
  delete/upsert outcome semantics), H2 (Java-only).
- CouchDB: Apache-2.0, absent (Couchbase ≠ CouchDB), REST/JSON → zero driver dependency, Mango
  filters. Lost: RavenDB Community (heavy client, license constraints).
- Chroma: Apache-2.0, absent, REST. Lost: LanceDB (prerelease .NET bindings, native deps).
- llama.cpp: MIT, local runtime (same class as shipped Ollama/LMStudio; ARCH-0127 gates *hosted*
  AI only), OpenAI-compatible endpoints.

## Run-book (hard-won; all verified this session)

**Build/test**
- The repo builds into ONE shared output root OUTSIDE the working tree, e.g.
  `%TEMP%/Koan-framework/tests/<Project>/bin/Debug/net10.0/`. One build at a time, always.
- For live test output run the xunit v3 exe directly:
  `<output-root>/<TestProject>/bin/Debug/net10.0/<Name>.Tests.exe -noColor` (optionally
  `-class <FullClassName>`). `dotnet test` buffers everything and hides hangs.
- If a suite stalls: `dotnet-stack report -p <pid>` (install: `dotnet tool install -g
  dotnet-stack`). An idle process with NO test thread = the fixture never finished, not a slow
  spec.

**Docker**
- Git Bash mangles absolute POSIX paths in `-e`/args. ALWAYS prefix container commands that carry
  paths with `MSYS_NO_PATHCONV=1`. This killed two container starts silently.
- An engine image with no HEALTHCHECK hangs `UntilContainerIsHealthy()` forever. Wait on the
  internal port (`UntilInternalTcpPortIsAvailable`) or a real endpoint (`/_up`,
  `/api/v2/heartbeat`). Check `docker inspect <image> --format '{{.Config.Healthcheck}}'` before
  trusting the default.

**Firebird container (reference invocation)**
```
MSYS_NO_PATHCONV=1 docker run -d --name koan-fb-probe \
  -e FIREBIRD_ROOT_PASSWORD=masterkey \
  -e FIREBIRD_DATABASE=/var/lib/firebird/data/probe.fdb \
  -e FIREBIRD_CONF_WireCrypt=Enabled \
  -e FIREBIRD_CONF_AuthServer="Srp256, Srp" \
  -e FIREBIRD_CONF_AuthClient="Srp256, Srp" \
  -p 3055:3050 firebirdsql/firebird:5.0.4
```
`FIREBIRD_CONF_*` keys are written into `firebird.conf` verbatim — they must match the file's
exact casing (`WireCrypt`, not `WIRECRYPT`). The image ignores `ISC_PASSWORD`; the SYSDBA
password is `FIREBIRD_ROOT_PASSWORD`. The .NET client cannot negotiate `WireCrypt=Required` or an
Srp256-only auth set.

**CouchDB container**: `couchdb:3.5`, `-e COUCHDB_USER -e COUCHDB_PASSWORD`, port 5984; admin
party disabled by default in 3.x. The fixture in `Koan.Data.Connector.CouchDb.Tests` is the
reference (generic Testcontainers builder + `/ _up` HTTP wait).

**Generated truth (regenerate + commit when the package set changes)**
```
dotnet run --project tools/Koan.Packaging -- quality --output docs/reference/package-quality.json
pwsh scripts/build-connector-matrix.ps1
pwsh scripts/skills-verify.ps1 -Structure
```
(`inventory` is a raw dump with trailing garbage — not the file's writer.) Not-assessed packages
get the ⚠ marker in the matrix and are NOT owed capability-map rows; a shelf row that names a
package must carry `**not assessed**` if unassessed.

**Package quality gate (README shape it greps for)**
- Title line exactly `# <PackageId>`; a real `dotnet add package <id>` expression; meaningful-use
  heading from the recognized keyword list ("what it adds", "usage", "behavior", "contract",
  ...); boundaries heading ("limits", "unsupported", "guarantees", ...). Write it right the first
  time.

**NativeAOT probe recipe (extend per adapter)**
```
scratch console (Microsoft.NET.Sdk.Web) + <PublishAot>true</PublishAot>
  + ProjectReferences to the adapters
dotnet publish -c Release -r win-x64
run the single binary against the real backend; publish success is NOT the claim — the run is
```
Status: consumer boots + serves 200; FirebirdClient wire round-trip (connect/DDL/UPDATE OR
INSERT/select/delete) exit 0. If new IL2026/IL3050 appear, prefer a source-generated
`JsonSerializerContext` or `[DynamicallyAccessedMembers]` at the flow source (see `KoanRegistry`
descriptors for the pattern; positional records need BOTH `[param:]` and `[property:]`).

**AOT-relevant framework facts**
- Discovered-descriptor Types are rooted via `DynamicallyAccessedMembers(PublicConstructors)` on
  `KoanRegistry`'s descriptor records — keep that pattern for any new registry surface.
- Lockfile JSON is source-generated (`KoanLockfileJsonContext`); keep new closed JSON shapes on
  source-gen, never reflection `JsonSerializer`.

## Standalone debug lessons (full versions live in docs/MEMORY.md dated 2026-08-29)

Probe before you build — the Firebird/CouchDB provider-fact logs live in each connector's
TECHNICAL.md. Divergence between sibling adapters is decided by reading the member, not majority
rule. Fallback facts are snapshot-keyed by code+subject, so a spec asserting "the fact appears"
must use a query shape no earlier spec ran. CouchDB stores managed discriminators under a legal
`koan.` subdocument (top-level `_`-prefixed members are rejected). `_bulk_docs` needs explicit
`_id` per doc or CouchDB assigns uuids silently. Mango bare equality on a collection is
parser-lowered to element-match (`$all`) — probe the composite path, not the raw operator.
