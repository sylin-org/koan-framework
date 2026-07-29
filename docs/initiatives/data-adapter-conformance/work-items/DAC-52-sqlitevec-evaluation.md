---
type: SPEC
domain: data
title: "DAC-52 Rebuild SqliteVec as the Embedded Vector Reference"
audience: [architects, maintainers, developers, ai-agents]
status: in-progress
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: behavior-green-control-plane-deferred
  scope: empty-root SqliteVec replacement, native win-x64 execution, Forge, and solution build
---

# DAC-52 — Rebuild SqliteVec as the embedded vector reference

| Field | Value |
|---|---|
| Phase / kind | vector / whole-adapter rebuild |
| Depends on | DAC-51 |
| Primer scope | Source Core and the ratified Vector manifest |
| Production writes | required inside the bounded replacement root |
| Owner | Adapter(SqliteVec); shared gaps require an executable gold case |

## Task

Replace the SqliteVec connector from an empty implementation root. Preserve only ratified package/configuration
identity and pinned native artifacts; derive every runtime part from the current Vector contract and measured sqlite-vec
behavior.

## Application intent

Store and search an Entity's embeddings in a durable local SQLite file through the ordinary Koan Vector expression,
without running a vector server or exposing extension-loading and SQL ceremony to the application.

## Public expression

Reference `Sylin.Koan.Data.Vector.Connector.SqliteVec`, then declare the source-owned space once:

```csharp
builder.Services.AddKoan(koan =>
    koan.Data.Source("Semantic").Vector<Article>(space => space
        .Name("articles")
        .Dimensions(1536)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session)));

await Vector<Article>.Save(article.Id, embedding, new { article.Category }, ct);
VectorPoint<string>? stored = await Vector<Article>.Get(article.Id, ct);
VectorSearchResult<string> related = await Vector<Article>.Search(
    embedding,
    query => query.Top(12).AtLeast(.82),
    ct);
await Vector<Article>.Delete(article.Id, ct);
```

When SQLite record storage is selected, SqliteVec pairs with that source's effective file. An independent vector file
requires only a source-scoped or adapter-default `ConnectionString`. Dimensions and metric remain space decisions;
there is no second SqliteVec metric setting.

## Guarantee and correction

An awaited save atomically replaces one complete point and is immediately visible to get and exact native search.
Results contain finite normalized similarity in descending order with stable identity ties and truthful execution
facts. File, table, dimension, metric, metadata, and scope belong to the immutable source/space decision.

Missing native support, wrong native version/hash, unsupported metric or visibility, corrupt/locked storage, incompatible
existing shape, unsupported query clause, and source-policy denial fail correctively. Read-only mutation rejects before
directory creation, file creation, extension extraction/loading, DDL, or transaction creation. `External` never creates,
repairs, or drops source shape.

## Complete intent surface

The package reference, one `AddKoan` declaration, optional source connection string/policy, optional source or partition
context, and `Vector<TEntity>` terminals are complete. There is no native library path, SQLite client, table name,
schema command, retry loop, explicit connection, or adapter disposal ceremony.

## Public concepts

- `Source` owns placement, access, lifecycle, and routing.
- `Name`, `Dimensions`, `Metric`, and `Visibility` are the complete vector-space decision.
- `ConnectionString` is the only SqliteVec-specific application override because physical placement cannot always be
  inferred.
- SqliteVec supports `Cosine` and `Euclidean`; `DotProduct` rejects because stable sqlite-vec `vec0` does not implement
  that metric.
- Metadata remains a neutral stored object. Arbitrary metadata filtering is declined because one JSON auxiliary value
  cannot satisfy filter-before-rank semantics natively.

## Docs read

- `docs/engineering/index.md` is a superseded pointer and contributes no adapter law.
- `docs/architecture/principles.md` requires intent-first public language, immutable composition, thin adapters,
  host-owned lifetime, and truthful claims.
- `docs/architecture/data-adapter-development-primer.md` owns whole-adapter `REBUILD`, Source Core, and V-01–V-24.
- `docs/toc.yml`, root `README.md`, and `samples/CATALOG.md` establish the Entity-centered public front door; the sample
  catalog itself is retired.
- `docs/reference/ai/vector.md` owns the current compact Vector curriculum and must remain provider-neutral.
- The sqlite-vec official release and API documentation establish stable `v0.1.9`, pre-v1 compatibility risk,
  `vec0` exact KNN, float32 blobs, cosine/L2 distances, `vec_version()`, and native metadata/partition constraints:
  <https://github.com/asg017/sqlite-vec/releases/tag/v0.1.9> and
  <https://alexgarcia.xyz/sqlite-vec/features/knn.html>.

## Code read

- `VectorService` binds source policy and immutable `VectorSpacePlan` before factory creation; this is the mandatory
  framework boundary.
- the new InMemory repository is the semantic oracle for complete points, normalized metrics, stable ordering, scope,
  batches, and execution truth; its storage mechanics do not transfer.
- `SqliteAdapterFactory`, `SqliteConnectionManager`, and `DataSourcePlan` establish paired placement and exact
  Managed/External plus ReadWrite/ReadOnly meanings; vector-native loading remains SqliteVec-owned.
- the retired SqliteVec repository discovers dimension from the first write, takes metric from a conflicting string
  option, holds one serialized connection, performs non-atomic single delete/insert, exposes legacy result shapes,
  omits scope from by-id operations, and normalizes non-cosine distance incorrectly.
- the retired route and health path create directories before policy-sensitive work; the retired native loader uses
  process-static mutable state and a fallback entrypoint.
- the existing SqliteVec suite baseline is 2 passed, 28 failed, and 28 skipped after DAC-51 because it has no compiled
  vector declarations and none of V-01–V-24 executes. This is failure evidence, not a fixture design to preserve.

## Reusing

- `VectorSpacePlan`, `VectorPoint`, `VectorScope`, `VectorSearchRequest`, `VectorSearchResult`, and `BatchResult`.
- `DataSourcePlan.Demand`, `AdapterConnectionResolver`, `VectorAdapterNaming`, and the one provider-election path.
- `Microsoft.Data.Sqlite` plus the already pinned SQLite native package.
- The embedded stable `v0.1.9` win-x64, linux-x64, and linux-arm64 binaries only after runtime version/hash proof.
- Shared Vector TestKit acceptance identities and the InMemory semantic oracle.

## Creating new

| New code | Location | Justification |
|---|---|---|
| plan-bound factory | `src/Connectors/Data/Vector/SqliteVec/SqliteVecAdapterFactory.cs` | resolves one immutable route without provider I/O |
| typed placement and safety limits | `src/Connectors/Data/Vector/SqliteVec/SqliteVecOptions.cs` | connection override and bounded metadata/search work |
| immutable route compiler | `src/Connectors/Data/Vector/SqliteVec/Runtime/SqliteVecRoute.cs` | one source/SQLite pairing decision shared by runtime and health |
| host-owned pinned native loader | `src/Connectors/Data/Vector/SqliteVec/Runtime/SqliteVecNative.cs` | one explicit RID/hash/version path with no process-static mutable decision |
| exact native repository | `src/Connectors/Data/Vector/SqliteVec/Runtime/SqliteVecRepository.cs` | complete points, scoped SQL, transactional mutation, native KNN, and disposal |
| policy-neutral health probe | `src/Connectors/Data/Vector/SqliteVec/SqliteVecHealthContributor.cs` | observes selected existing storage without provisioning it |
| neutral metadata writer | `src/Koan.Data.Vector.Abstractions/VectorMetadata.cs` | provider-neutral inverse of the existing neutral JSON decoder |
| executable SqliteVec Vector cells | SqliteVec Vector test project | proves the native gold behavior without copying retired fixture structure |

## Coalescence

Closest patterns are the plan-bound InMemory Vector adapter and the rebuilt SQLite record adapter. The Vector pillar
owns semantic validation, source policy, metadata materialization, and query intent. SqliteVec owns only native binary,
connection, schema, codec, transaction, and SQL execution mechanics. SQLite record internals are the wrong narrower
owner because SqliteVec can ship independently; Data Core is the wrong wider owner because extension loading and
`vec0` SQL are provider facts.

Disposition is `REBUILD` for the complete adapter implementation, `KEEP` only for external package/configuration
identity and verified native payloads, and `DELETE` for every old factory/repository/route/loader/health/module body and
legacy fixture assumption. No compatibility repository, shadow table path, managed brute-force fallback, adapter-local
metric option, or process-static runtime decision survives.

## Ergonomics

Application code reads identically to InMemory and later networked providers. IntelliSense exposes only the shared
space and query language. Adding SqliteVec changes durability and native execution, not the model or terminals.
Provider-specific failures name the unavailable guarantee and correction without leaking raw SQL or native paths.

## Constraints satisfied

- Entity remains the application center; no repository or provider client enters user code.
- No HTTP surface is added.
- Stable provider, configuration, native, and SQL identifiers live in project constants; tunables live in typed options.
- Structure compiles once; warm calls consume the bound plan and route.
- Search is exact native `vec0`; there is no managed scan or silent fallback.
- README, TECHNICAL, module report, capabilities, tests, and this card change with behavior.

## Risks

- sqlite-vec is pre-v1; the stable binary and runtime `vec_version()` must agree exactly.
- `vec0` upsert is delete plus insert; Koan must wrap both in one SQLite transaction and prove rollback.
- Stable identity ties may require bounded native candidate expansion; exceeding the configured bound must reject rather
  than return nondeterministic results.
- File-mode normalization must prevent reads and health probes from creating a missing source.
- Only win-x64 is executable in this session. Linux binary hashes and package inclusion are provable here; Linux native
  execution remains an explicit external runner requirement.

## Greenfield boundary

Delete every current SqliteVec implementation file before authoring replacements. Retain only the `.csproj`, package
identity/version file, and three native resources as frozen external inputs. Do not port or mechanically transform the
retired type graph, helper layout, control flow, caches, repository lock, SQL text, fixture layout, or comments.

## Verification

- Prove all applicable Source Core and V-01–V-24 cells on the real embedded extension with no certification skips.
- Compare cosine and Euclidean results with the InMemory oracle within explicit float tolerance.
- Prove native version/hash, query plan, exact execution, stable ties, atomic replace/batch rollback, positional get,
  metadata, scope, cancellation, lock/corruption, disposal, restart durability, and bounded warm behavior.
- Prove ReadOnly and External reject before every source mutation, including implicit file/table creation.
- Run strict Forge where its packet generator is available; do not hand-build a second evidence system.
- Run the full solution build and every directly affected suite.

### Result

| Check | Result |
|---|---|
| SqliteVec complete suite | PASS — 58 passed; five capability-matrix skips for deliberately unclaimed export/stats/hybrid/filter features |
| V-01–V-24 plus G-09 ledger | PASS — 28 passed, zero skipped, real embedded vec0 |
| adapter-specific reference proofs | PASS — cosine, Euclidean, DotProduct decline, complete neutral scalar kinds, restart durability, capability truth, and candidate bound |
| InMemory Vector regression | PASS — 50 passed, zero skipped |
| non-strict Forge | GREEN — all 28 SqliteVec vector cells |
| strict Forge | DEFERRED — behavior remains green; `evidence/sqlitevec/conformance.json` is not generated |
| full `Koan.sln` build | PASS — zero warnings, zero errors |
| initiative structural script | RED outside DAC-52 behavior — existing progress/roadmap dependency inconsistencies and concurrent DAC-51 state |

The shipped win-x64 payload executed and proved `vec_version() = v0.1.9`; all three embedded RID payloads match their
pinned SHA-256 values. Linux x64 and arm64 execution remains an external-runner evidence item, not a hidden green claim.

## Definition of done

- [x] The old implementation root and legacy paths are absent.
- [x] One plan-bound native execution path implements the compact Vector contract.
- [x] Stable `v0.1.9` identity and packaged RID payloads are verified.
- [x] Every claimed capability has executable native evidence; every decline fails correctively.
- [x] Source lifecycle/access, isolation, durability, failure, and hot-path behavior are green.
- [x] README, TECHNICAL, module report, and claims tell the same truth.
- [x] Full solution build and scoped retirement checks are green.
- [ ] Strict versioned packet validation is green or remains an explicit control-plane blocker.

## Stop conditions

Old implementation reuse, process-static mutable runtime state, pre-gate source mutation, string-configured metric,
managed brute-force fallback, raw or negative provider score, unstable ties, unbounded candidate expansion, fake totals,
silent filter residuals, schema repair under `External`, or a second execution path stops work.
