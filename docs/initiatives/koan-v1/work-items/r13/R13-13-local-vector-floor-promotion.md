---
type: SPEC
domain: framework
title: "R13-13 - Promote the local Vector foundation"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  status: in-progress
  scope: focused Vector runtime, InMemory, sqlite-vec, pack, consumer, product, and API evidence
---

# R13-13 — Promote the local Vector foundation

## Architecture checkpoint

**Task:** Promote the provider-neutral Vector runtime and the two local providers as one useful 0.20
family, while capturing CockroachDB's exact public API floor.

**Application intent:** An application adds one local vector-provider package, calls `AddKoan()`, and
stores and searches Entity embeddings without provider-registration ceremony or a vector server.

**Public expression:** The smallest ephemeral expression is one package reference plus the normal host
bootstrap and `Vector<TEntity>` facade:

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.InMemory
```

```csharp
builder.Services.AddKoan();

public sealed class Article : Entity<Article>;

await Vector<Article>.Save("koan", [1f, 0f, 0f]);
var nearest = await Vector<Article>.Search([0.9f, 0.1f, 0f], topK: 5);
```

Replacing the package with `Sylin.Koan.Data.Vector.Connector.SqliteVec` keeps the same code and adds
embedded durability. Configuration is optional unless sqlite-vec should not pair with the effective
SQLite placement. sqlite-vec requires a supported bundled RID (`win-x64`, `linux-x64`, or
`linux-arm64`).

**Guarantee/correction:** The Vector runtime owns exact provider election, per-host repository
memoization, source/partition/tenant folds, capability-gated operations, and participation reporting.
InMemory guarantees bounded process-local exact cosine search and honest data loss at process exit.
sqlite-vec guarantees embedded durable kNN on supported RIDs, participation-aware readiness, and
fail-closed isolation when metadata filtering is required. Missing exact providers, unsupported
capabilities/RIDs/metrics, mixed dimensions, and unavailable selected storage fail explicitly; Koan
does not silently select an unrelated provider or fabricate durability.

**Complete intent surface:** One provider package; ordinary `AddKoan()`; a string-keyed Entity type;
application/model-produced `float[]` embeddings; `Vector<TEntity>.Save/Search`; optional existing
provider/default/source settings; and, for sqlite-vec, a supported native RID and writable selected
placement. There is no manual provider registration, repository resolution, or new public setup API.

**Public concepts:** Existing `Vector<TEntity>` expresses the Entity-centered vector decision;
`VectorQueryOptions` and `VectorCaps` express query intent and optional guarantees;
`VectorDefaultsOptions`, `[VectorAdapter]`, and `SqliteVecOptions` express deliberate routing or
placement overrides. No new public concept is required. Product truth splits the Vector foundation
from the still-experimental AI runtime so support does not leak across unrelated readiness.

**Docs read:**

- `docs/engineering/index.md` — requires Entity-first use, focused owner evidence, package hygiene,
  centralized identifiers, and proportionate validation; directly applicable.
- `docs/architecture/principles.md` — makes package reference the availability declaration and the
  Vector pillar the decision owner; directly applicable.
- `docs/decisions/ARCH-0120-terminal-package-maturity.md` — defines cohesive family promotion and
  provider-specific real-boundary/consumer proof without an admission bureaucracy; governing.
- `docs/reference/cards/vector.md` — freezes `Vector<TEntity>.Save/Search`, election, capability, and
  participation behavior as the canonical application surface; directly applicable.
- `src/Koan.Data.Vector/{README,TECHNICAL}.md` and local-provider companions — define the runtime,
  ephemeral InMemory floor, durable sqlite-vec placement, native limits, and non-claims; directly
  applicable.

**Code read:**

- `Vector.cs`, `VectorService.cs`, and `VectorProviderCatalog.cs` — own the public facade, exact
  repository resolution, memoization, and direct-reference/automatic-floor election; keep.
- `InMemoryVectorAdapterFactory.cs` and `InMemoryVectorRepository.cs` — own the managed automatic
  floor and convergence-oracle mechanics; keep, centralizing its stable provider identities.
- `SqliteVecAdapterFactory.cs`, `SqliteVecVectorRepository.cs`, and
  `SqliteVecHealthContributor.cs` — own route reuse, vec0 mechanics, honest capabilities, and
  participation-aware health; keep.
- `VectorAdapterSurfaceSpecsBase.cs` and the InMemory/Qdrant cells — the existing family-conformance
  owner and closest provider-cell pattern; absorb sqlite-vec into it.

**Reusing:** Existing Vector facades/contracts, provider catalog, participation health base, naming
pipeline, local repositories/options, shared VectorAdapterSurface TestKit, package compiler, API
guard, and main publisher all already exist. New runtime/options/contracts are unnecessary.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| InMemory provider constants | `src/Connectors/Data/Vector/InMemory/Infrastructure/Constants.cs` | Centralize stable provider identity, aliases, and priority in the owning adapter. |
| sqlite-vec matrix factory | `tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.SqliteVec.Tests/SqliteVecTestFactory.cs` | Bind the real embedded provider and its honest capability set to the existing family TestKit. |
| sqlite-vec matrix specs | `tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.SqliteVec.Tests/SqliteVecMatrixSpecs.cs` | Reuse the shared CRUD/search/partition/semantic contract without another test framework. |

**Coalescence:** Closest pattern:
`Koan.Data.VectorAdapterSurface.InMemory.Tests/InMemoryVectorTestFactory.cs`. Vector Core remains the
family-law owner; each connector retains backend mechanics and lifetime. InMemory and sqlite-vec have
different durability/filter/native guarantees, so their repositories stay adapter-specific, while
the identical application semantics belong in the existing family matrix. Disposition: keep the
runtime and both adapters; absorb sqlite-vec into the matrix; split the mixed AI/Vector product claim;
delete no runtime path. Remove redundant direct package edges only if the staged artifact proves they
conflict with the supported transitive owner—never suppress NuGet downgrade diagnostics.

**Ergonomics:** One package reference and `Vector<TEntity>.Save/Search` remain readable and
IntelliSense-discoverable. The only user branch is ephemeral InMemory versus durable sqlite-vec;
optional provider/capability/placement concepts appear only when the guarantee actually changes.
No composition internals, factories, or repositories enter the common path.

**Constraints satisfied:**

- Entity-centered `Vector<TEntity>` facade; no repository ceremony in the common path.
- No HTTP surface or inline endpoint.
- Stable new identifiers live in adapter-scoped constants; tunables remain typed options.
- Bounded `topK` search and existing explicit export behavior; no unbounded Entity read is added.
- README/TECHNICAL and product/reference docs update with the support claim.
- Focused local family suites, staged artifact consumer, package/API checks, and cheap coherence only;
  no network-provider matrix or whole-framework certification.

**Risks:** sqlite-vec's native resource packaging and direct dependency floors must be proven from the
staged nupkg on all bundled asset paths; this workstation can execute only its current RID. InMemory is
not a production-scale or durable store, and sqlite-vec is not distributed or filter-capable. Those
limits remain explicit rather than normalized away.

## Evidence boundary

1. Run the Vector runtime unit owner plus the complete InMemory and sqlite-vec cells with zero
   infrastructure skips; record explicit unsupported-capability skips rather than treating them as
   provider failures.
2. Pack the four owners with `PublicRelease=true`; inspect their nuspec and embedded native assets.
3. Restore/build/run a clean external consumer from the staged artifacts in a fresh cache and prove
   both ephemeral InMemory search and sqlite-vec restart durability through normal `AddKoan()`.
4. Compile product truth, run the API guard and no-tests coherence; do not run external vector cells.
5. After `main` publication, rerun the same consumer from NuGet.org and record exact first versions as
   immutable API floors in the next family slice.

## Focused evidence — 2026-07-22

- product boundary: split the supported Vector foundation from the still-experimental AI runtime;
  local provider guarantees remain one cohesive claim with distinct durability/capability limits;
- InMemory family cell: 34/34 passed with zero skips;
- sqlite-vec family cell: 29 passed with five explicit unsupported-capability skips (metadata filters,
  hybrid search, export, and stats); participation health and all three AODB modes are included;
- matrix-discovered correction: sqlite-vec's existing single embedding lookup now has bounded batch
  retrieval under the same connection lock; unknown IDs remain omitted;
- four `PublicRelease=true` packs: exact local first versions are all `0.20.0`; dependency bands align
  with the supported foundation, so no redundant-edge suppression or release plumbing was added;
- native artifact shape: the compiled sqlite-vec package assembly contains `vec0.win-x64`,
  `vec0.linux-x64`, and `vec0.linux-arm64`; the current Windows asset executed in both tests and the
  package consumer;
- external staged-package consumer: restored the InMemory data provider plus both local Vector
  providers into a fresh cache, built with zero warnings/errors, selected providers through normal
  `AddKoan()`/attributes, proved InMemory loss and sqlite-vec survival across complete host restart,
  and passed exact batch retrieval with
  `LOCAL-VECTOR|PACKAGE-CONSUMER|INMEMORY-EPHEMERAL|SQLITEVEC-DURABLE|BATCH-GET|PASS`;
- generated product truth: 41 claims / 93 packages;
- API posture: 52/56 configured, four allowed first-publication pending floors, and three content-only
  owners;
- no-tests coherence: release build, committed composition lockfiles, documentation truth/lint,
  diff-scoped code validation, skills lint, and blueprint lint all passed in 20.2 seconds;
- no external Vector provider, provider matrix beyond the two local cells, or whole-framework
  certification ran.
