---
type: GUIDE
domain: framework
title: "Connector fleet — ledger"
audience: [ai-agents, maintainers]
status: current
last_updated: 2026-08-19
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-19
  status: verified
  scope: durable progress record and resume point
---

# Ledger

The one authoritative record of progress. If this file and your recollection disagree, this file is
right. Update it in the same commit as the work it describes, or immediately after recording BLOCKED.

## RESUME HERE

> **Next task:** none — initiative complete
> **State:** complete; T1 through T4 are Done and no task is in progress
> **Last commit touching this initiative:** this commit (`feat(connector): mongo atlas vector search on the vector plane`)
>
> Do not extend this fleet without a new decision under ARCH-0127.

Whoever picks this up next: update the three lines above **before** you start, so an interruption
leaves an accurate resume point rather than a stale one.

## Status

| # | Task | State | Commit | Oracle exit |
|---|---|---|---|---|
| T1 | pgvector | Done | this commit | 0 |
| T2 | Redis vector | Done | this commit | 0 |
| T3 | MySQL / MariaDB | Done | this commit | 0 |
| T4 | Mongo Atlas Vector | Done | this commit | 0 |

States: `Not started` · `In progress` · `Done` · `BLOCKED`.

## Log

One entry per task attempt. Append; never rewrite history. Copy this template:

```
### T<n> — <task name> — <Done | BLOCKED> — <date>

Commit: <sha or "none">
Oracle: <literal command> -> exit <code>
Acceptance: skills-verify <pass/fail> · docs-lint <Errors: n> · build <pass/fail> · discoverability <done/not>

Deviations:
1. <what differed from the task prompt, and what you did about it>
2. ...
(or: none)

Notes: <anything the next executor needs. For BLOCKED, what is required to unblock.>
```

A deviation is anything where the tree contradicted the prompt, where you made a judgement the prompt
did not pin, or where you touched a file the prompt did not name. Recording one is neutral — it is the
feedback channel that improves the next batch, not an admission of error. An entry with `none` is
equally valid; do not invent deviations to seem thorough.

---

<!-- Entries begin below. Newest last. -->

### T1 — pgvector — BLOCKED — 2026-08-19

Commit: none
Oracle: not run — STOP precondition 5 failed
Acceptance: skills-verify not run · docs-lint not run · build not run · discoverability not

Deviations: none

Notes: STOP precondition 5 failed because no available container engine could start a pgvector image.
The exact runtime check was:

```text
docker version --format '{{json .}}'
```

It exited 1 with:

```text
{"Client":{"Version":"29.5.3","ApiVersion":"1.54","DefaultAPIVersion":"1.54","GitCommit":"d1c06ef","GoVersion":"go1.26.4","Os":"windows","Arch":"amd64","BuildTime":"Wed Jun  3 18:03:06 2026","Context":"default"},"Server":null}
WARNING: Error loading config file: open C:\Users\onose\.docker\config.json: Access is denied.
failed to connect to the docker API at npipe:////./pipe/docker_engine; check if the path is correct and if the daemon is running: open //./pipe/docker_engine: The system cannot find the file specified.
```

`podman` and `nerdctl` were not installed. `com.docker.service` existed but was stopped. The attempted
command `Start-Service -Name 'com.docker.service' -ErrorAction Stop` also exited 1 with `Service
'Docker Desktop Service (com.docker.service)' cannot be started due to the following error: Cannot open
'com.docker.service' service on computer '.'.` Unblocking requires an accessible running container
engine; then start a Postgres image containing the `vector` extension and rerun T1 from its STOP checks.

### T2 — Redis vector — BLOCKED — 2026-08-19

Commit: none
Oracle: not run — STOP precondition 4 failed
Acceptance: skills-verify not run · docs-lint not run · build not run · discoverability not

Deviations: none

Notes: STOP precondition 4 failed because the installed Docker client could not reach an engine, so an
image providing Redis vector search could not start. The exact runtime check was:

```text
docker version --format '{{json .}}'
```

It exited 1 with:

```text
{"Client":{"Version":"29.5.3","ApiVersion":"1.54","DefaultAPIVersion":"1.54","GitCommit":"d1c06ef","GoVersion":"go1.26.4","Os":"windows","Arch":"amd64","BuildTime":"Wed Jun  3 18:03:06 2026","Context":"default"},"Server":null}
WARNING: Error loading config file: open C:\Users\onose\.docker\config.json: Access is denied.
failed to connect to the docker API at npipe:////./pipe/docker_engine; check if the path is correct and if the daemon is running: open //./pipe/docker_engine: The system cannot find the file specified.
```

The other STOP checks passed: the vector kit and Qdrant reference exist, and the tree contains both
`src/Connectors/Data/Redis/` and `src/Koan.Redis/`. `podman` and `nerdctl` were not installed. T1's
attempt to start `com.docker.service` had already failed because this session cannot open the service.
Unblocking requires an accessible running container engine and a Redis image with vector search.

### T3 — MySQL / MariaDB — BLOCKED — 2026-08-19

Commit: none
Oracle: not run — STOP precondition 4 failed
Acceptance: skills-verify not run · docs-lint not run · build not run · discoverability not

Deviations: none

Notes: STOP precondition 4 failed because the installed Docker client could not reach an engine, so a
MySQL or MariaDB image could not start. The exact runtime check was:

```text
docker version --format '{{json .}}'
```

It exited 1 with:

```text
{"Client":{"Version":"29.5.3","ApiVersion":"1.54","DefaultAPIVersion":"1.54","GitCommit":"d1c06ef","GoVersion":"go1.26.4","Os":"windows","Arch":"amd64","BuildTime":"Wed Jun  3 18:03:06 2026","Context":"default"},"Server":null}
WARNING: Error loading config file: open C:\Users\onose\.docker\config.json: Access is denied.
failed to connect to the docker API at npipe:////./pipe/docker_engine; check if the path is correct and if the daemon is running: open //./pipe/docker_engine: The system cannot find the file specified.
```

The other STOP checks passed: the record conformance kit and Postgres structural reference exist, and
the tree contains `src/Koan.Data.Relational/` plus `src/Koan.Data.Relational.Abstractions/`.
Unblocking requires an accessible running container engine and a startable MySQL or MariaDB image.

### Initiative stopped — 2026-08-19

T1, T2, and T3 are three consecutive BLOCKED tasks with the same unavailable-container-runtime cause.
BOOTSTRAP's failure protocol says this indicates an initiative-level environment problem and requires
the executor to stop. T4 was therefore not opened or attempted, remains `Not started`, and the
initiative is not complete. Resume only after a container engine is accessible in the execution
session; then review the three BLOCKED entries and restart at the appropriate task under BOOTSTRAP.

### T1 — pgvector retry — BLOCKED — 2026-08-19

Commit: none
Oracle: not run — implementation-readiness verification found an unsatisfiable conformance contract
Acceptance: skills-verify not run · docs-lint not run · build not run · discoverability not

Deviations:
1. The current vector kit has 24 provider proof-seam facts that were not reflected in T1's required
   BootHost-only subclass shape. Every current sibling overrides the additional proof seam.

Notes: Docker recovery was verified before this attempt: `pgvector/pgvector:pg16` started successfully,
PostgreSQL accepted connections, and `pg_available_extensions` reported vector `0.8.6`. Step 3 then
found that T1 cannot satisfy both its required artifact and its exit-0 oracle without inventing
provider-specific expectations. The exact verification command was:

```powershell
$kit='tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.TestKit/VectorAodbConformanceSpecsBase.cs'; Select-String -LiteralPath $kit -Pattern 'protected virtual Task ProveVectorAnnexCellAsync|Assert.Skip\(' | ForEach-Object { '{0}:{1}' -f $_.LineNumber,$_.Line.Trim() }; Write-Output ('PROOF_SEAM_FACTS=' + (Select-String -LiteralPath $kit -Pattern '=> ProveVectorAnnexCellAsync\(').Count); $qdrant='tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.Qdrant.Tests/QdrantVectorAodbConformanceSpec.cs'; Select-String -LiteralPath $qdrant -Pattern 'protected override Task ProveVectorAnnexCellAsync' | ForEach-Object { '{0}:{1}' -f $_.LineNumber,$_.Line.Trim() }
```

It exited 0 and showed:

```text
155:protected virtual Task ProveVectorAnnexCellAsync(string acceptanceId, string proof)
158:Assert.Skip($"{acceptanceId} provider proof seam is registered but not yet supplied: {proof}.");
PROOF_SEAM_FACTS=24
62:protected override Task ProveVectorAnnexCellAsync(string acceptanceId, string proof)
```

`scripts/forge-verify.ps1` maps every skipped outcome to `INCONCLUSIVE` and exit 2. T1 requires the
PgVector subclass to override `BootHostAsync()` only, while the default proof seam skips; copying
Qdrant's extra override would violate the task and derive PgVector expectations that the prompt does
not pin. The kit and runner are NEVER-touch files. Unblocking requires revised task authority that
explicitly permits and pins the PgVector V-01 through V-24 provider proofs against the current kit.

### T2 — Redis vector retry — BLOCKED — 2026-08-19

Commit: none
Oracle: not run — implementation-readiness verification found an unsatisfiable conformance contract
Acceptance: skills-verify not run · docs-lint not run · build not run · discoverability not

Deviations:
1. The current vector kit has 24 provider proof-seam facts that were not reflected in T2's required
   BootHost-only subclass shape. Every current sibling overrides the additional proof seam.

Notes: All STOP preconditions passed. `redis/redis-stack-server:latest` started successfully,
`redis-cli PING` returned `PONG`, and `MODULE LIST` reported the `search` module at version `21020`.
Step 3 then found that T2 cannot satisfy both its required artifact and exit-0 oracle. The exact
verification command was:

```powershell
$task='docs/initiatives/connector-fleet/tasks/T2-redis-vector.md'; Select-String -LiteralPath $task -Pattern 'overriding `BootHostAsync\(\)` only' | ForEach-Object { '{0}:{1}' -f $_.LineNumber,$_.Line.Trim() }; $kit='tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.TestKit/VectorAodbConformanceSpecsBase.cs'; Select-String -LiteralPath $kit -Pattern 'protected virtual Task ProveVectorAnnexCellAsync|Assert.Skip\(' | ForEach-Object { '{0}:{1}' -f $_.LineNumber,$_.Line.Trim() }; Write-Output ('PROOF_SEAM_FACTS=' + (Select-String -LiteralPath $kit -Pattern '=> ProveVectorAnnexCellAsync\(').Count)
```

It exited 0 with:

```text
56:`VectorAodbConformanceSpecsBase`, overriding `BootHostAsync()` only.
155:protected virtual Task ProveVectorAnnexCellAsync(string acceptanceId, string proof)
158:Assert.Skip($"{acceptanceId} provider proof seam is registered but not yet supplied: {proof}.");
PROOF_SEAM_FACTS=24
```

`scripts/forge-verify.ps1` maps every skipped outcome to exit 2. Adding the proof override would
violate T2 and derive RedisVector expectations that the prompt does not pin; the kit and runner are
NEVER-touch files. Unblocking requires revised task authority that explicitly permits and pins the
RedisVector V-01 through V-24 provider proofs against the current kit.

### T3 — MySQL / MariaDB retry — BLOCKED — 2026-08-19

Commit: none
Oracle: not run — step 3 found an unsatisfiable package-versioning contract before implementation
Acceptance: skills-verify not run · docs-lint not run · build not run · discoverability not

Deviations:
1. The current packaging tool requires every new packable project to own a project-local
   `version.json`, but BOOTSTRAP forbids touching any `version.json` with no sanctioned exception.
   T3's required MySql package therefore cannot enter the inventory or pass package quality.
2. T3 says record-plane test projects live under `tests/Suites/Data/AdapterSurface/`; current concrete
   suites instead live under `tests/Suites/Data/Connector.<Name>/`.
3. T3 identifies `store-and-expose.md` as the as-of-authoring store-choice recipe. The current recipe
   scan also finds server-store choice lists in `model-things-that-relate.md` and
   `publish-to-a-named-channel.md`; all three would need MySql if the task were unblocked.

Notes: All numbered STOP preconditions passed. Docker started `mysql:8.4`, `mysqladmin ping` reported
the server alive, and MySQL reported version `8.4.11`. Exploration stopped before production edits
when the required connector artifact was compared with the NEVER-touch table and packaging
enforcement. The exact verification command was:

```powershell
$task = Select-String -LiteralPath 'docs/initiatives/connector-fleet/tasks/T3-mysql.md' -Pattern 'Connector: `src/Connectors/Data/MySql/`'; $never = Select-String -LiteralPath 'docs/initiatives/connector-fleet/BOOTSTRAP.md' -Pattern 'Any `version.json`'; $versioning = Select-String -LiteralPath 'docs/engineering/versioning.md' -Pattern 'A new packable project needs its own `version.json`'; $source = Get-Content -LiteralPath 'tools/Koan.Packaging/Services/RepositoryInspector.cs'; 'TASK ' + $task.LineNumber + ': ' + $task.Line.Trim(); 'BOOTSTRAP ' + $never.LineNumber + ': ' + $never.Line.Trim(); 'VERSIONING ' + $versioning.LineNumber + ': ' + $versioning.Line.Trim(); for ($i=77; $i -le 82; $i++) { 'PACKAGING ' + ($i+1) + ': ' + $source[$i].Trim() }; 'RESULT=T3 cannot add its required packable project without a forbidden version.json; package-quality would reject the inherited root owner.'; exit 1
```

It exited 1 with:

```text
TASK 57: - Connector: `src/Connectors/Data/MySql/`, project `Koan.Data.Connector.MySql.csproj`, package
BOOTSTRAP 130: | Any `version.json`, or any hand-written package version | **None.** NBGV owns versions. |
VERSIONING 50: A new packable project needs its own `version.json` before it can join the inventory. Copy one from a
PACKAGING 78: if (!string.Equals(versionOwner, expectedOwner, StringComparison.OrdinalIgnoreCase))
PACKAGING 79: {
PACKAGING 80: throw new InvalidOperationException(
PACKAGING 81: $"Packable package '{packageId}' owned by '{Relative(project)}' resolves versioning from " +
PACKAGING 82: $"'{versionOwner}'. Add '{expectedOwner}' so the package owns its own version and only " +
PACKAGING 83: $"its own changes advance it.");
RESULT=T3 cannot add its required packable project without a forbidden version.json; package-quality would reject the inherited root owner.
```

No production, conformance, generated, or version files were changed. Unblocking requires task
authority to add a narrow exception permitting creation of the mandatory project-local `version.json`,
or a repository versioning change that lets a new packable connector pass package quality without one.

### Initiative stopped — 2026-08-19

The recovered-runtime retries of T1, T2, and T3 are three consecutive BLOCKED tasks. T1 and T2
conflict with the current vector proof-seam kit; T3 conflicts with the repository's mandatory
project-local version ownership and the initiative's absolute version-file prohibition. BOOTSTRAP's
failure protocol therefore requires the executor to stop. T4 was not opened or attempted and remains
`Not started`; the initiative is not complete. Resume only after the task authority is reconciled with
the current conformance and packaging contracts.

### Initiative resumed — 2026-08-19

The maintainer explicitly authorized repairing requirements that were misaligned with the initiative's
user outcome and requested delivered connectors. BOOTSTRAP now permits only the mandatory NBGV file
for a new packable project, requires a committed expected-outcome profile when the current kit has
new proof seams, and treats three blockers as an authority audit rather than a magical stop. T1, T2,
and T4 now authorize the inherited provider-proof hook without changing any shared `[Fact]`; T3 now
uses the concrete record-suite location present in the tree. Historical BLOCKED attempts above remain
unchanged. Execution resumes at T1 in the original order.

### T1 — pgvector — Done — 2026-08-19

Commit: this commit (`feat(connector): pgvector on the vector plane`)
Oracle: `pwsh scripts/forge-verify.ps1 -Adapter PgVector -Plane vector` -> exit 0 (28 passed, 0 failed, 0 skipped)
Acceptance: skills-verify pass · docs-lint Errors: 0 · build pass with 0 warnings · discoverability done · package quality regenerated

Deviations:
1. The as-authored BootHost-only test shape contradicted the current 24-cell provider proof seam. The
   maintainer-authorized requirement repair pinned V-01 through V-24; T1 implements them without adding,
   replacing, or skipping shared facts.
2. Qdrant's mirrored layout names a separate client wrapper. PgVector uses direct, parameterized Npgsql
   commands in the repository because a pass-through client would add no lifecycle or semantic value.
3. A new package cannot validate against its own unpublished `1.0.0` baseline. `Directory.Build.targets`
   gained the default-on `KoanHasPublishedBaseline` switch, and only this new project opts out until it has
   a published baseline.
4. Shared storage naming does not emit the provider key, so record and vector tables for one Entity would
   collide in the same Postgres database. PgVector uses a deterministic `_vector` anchor suffix and the
   live V-03 proof saves both representations side by side.
5. Lossless neutral metadata uses PostgreSQL `json`, not `jsonb`, because `jsonb` collapses duplicate keys
   and property order. A separate `jsonb` projection owns native filtering.
6. The fixture uses `pgvector/pgvector:pg16` because the task did not pin an image digest. The test project
   references the existing Postgres record connector only to prove the core no-second-service outcome and
   cross-store rejection; production connectors remain independent.
7. V-24's thread-local allocation counter was invalid across asynchronous continuations. The proof profile
   now measures total managed allocation across the complete save/get/search cycles.
8. The new-package baseline switch and the async-safe V-24 wording touch central files not named by T1;
   both were required for truthful green acceptance rather than task-specific scaffolding.

Notes: The connector reuses explicit, discovered, named, and partial-URI Postgres placement; preserves
record/vector coexistence; installs pgvector under a database-wide first-use lock; validates native shape;
forces exact scans even with an ANN index present; pushes filters natively; and performs set-based bulk
save/delete. The full solution build completed with 0 warnings and package quality reports 95 packages,
0 repair, 10 review, and 85 structurally ready.

### T2 — Redis vector — Done — 2026-08-19

Commit: this commit (`feat(connector): redis vector search on the vector plane`)
Oracle: `pwsh scripts/forge-verify.ps1 -Adapter RedisVector -Plane vector` -> exit 0 (28 passed, 0 failed, 0 skipped)
Acceptance: skills-verify pass · docs-lint Errors: 0 · build pass · discoverability done · package quality regenerated

Deviations:
1. The original BootHost-only test shape contradicted the current 24-cell provider proof seam. The
   maintainer-authorized requirement repair pins V-01 through V-24; the concrete suite implements that
   proof hook while leaving every shared `[Fact]` inherited and unchanged.
2. The Qdrant reference has connector-owned discovery, service metadata, and a client wrapper. RedisVector
   deliberately has none: it reuses `Sylin.Koan.Redis` as the single discovery, multiplexer, pooling, and
   disposal owner, and executes native commands directly through its shared connection provider.
3. Redis Search 2.10.20 accepts vector indexes only in logical database 0. RedisVector therefore keeps its
   indexes in DB 0 while records and cache entries may use another logical database on the same Redis process;
   provider-scoped routing still coalesces on the same named endpoint.
4. The task did not pin a fixture image. The proof uses the immutable Redis Stack digest
   `sha256:798ab84d9f266936b034ab11c4d04a2b8e4b441884c5aa7d17ac951eefdf742a`, verifies Search 2.10.20,
   and enables synchronous AOF so V-23 exercises real restart durability.
5. Redis negotiates RESP3 by default through the shared StackExchange.Redis client, whereas most command
   examples document RESP2. The repository parses both native result shapes and the fixture proves the live
   RESP3 path.
6. RediSearch has a fixed schema rather than arbitrary JSON paths. Native filtering uses bounded hashed TAG
   projections plus lazily added NUMERIC fields, with cross-process locking and cache refresh. Finite values
   that cannot be represented exactly by Redis numerics remain lossless metadata but relational filters on
   them fail closed instead of silently changing meaning.
7. Readiness uses a low-privilege `FT.INFO` probe rather than admin-only `FT._LIST`; exact vector algorithm,
   type, dimension, metric, prefixes, and filter field shapes are validated at the first managed use.
8. Only `search-by-meaning.md` is currently a genuine vector-provider choice recipe. SQLite-specific recipes
   were not broadened simply to mention the new package.

Notes: RedisVector coexists with the record and cache planes on one vector-enabled Redis deployment, owns
disjoint vector keys, reports exact FLAT execution, pushes the complete declared filter algebra natively,
supports session-visible pipelined bulk operations, and rejects unsupported intent correctively. The full
solution compiled with 0 errors; NuGet's vulnerability endpoint was unavailable during final acceptance, so
the build emitted only external `NU1900` audit warnings and no compiler or connector warnings. Package quality
reports 96 packages, 0 repair, 10 review, and 86 structurally ready.

### T3 — MySQL / MariaDB — Done — 2026-08-19

Commit: this commit (`feat(connector): mysql on the record plane`)
Oracle: `pwsh scripts/forge-verify.ps1 -Adapter MySql -Plane record` -> exit 0 (6 passed, 0 failed, 0 skipped)
Acceptance: skills-verify pass · docs-lint Errors: 0 · build pass with 0 warnings · discoverability done · package quality regenerated

Deviations:
1. Concrete record suites live under `tests/Suites/Data/Connector.<Name>/`, not the as-authored AdapterSurface
   location. The repaired task and MySQL suite follow the current tree without changing the shared facts.
2. Shared relational code owns mapping, structured values, schema policy, and filter translation, but its Npgsql
   executor cannot lower MySQL SQL. The connector keeps only the provider-specific dialect, schema, and execution
   path local; no shared relational or existing connector code changed.
3. The fixture is test-local rather than expanding the shipped container harness for one suite. It pins MySQL
   8.4.11 by digest and uses root credentials because the inherited database-isolation proof creates two databases.
4. The current recipe scan found three genuine entity-store choice recipes, not only `store-and-expose.md`; all
   three now list MySQL, while the SQLite-specific single-binary recipe remains unchanged.
5. MySqlConnector 2.6.1 joins the existing Testcontainers 4.13.0 dependency train. The new package uses the
   initiative's narrow NBGV/new-package baseline exceptions; no existing version owner was changed.
6. MySQL's default `LIKE` escape literal and JSON boolean coercion differ from the shared neutral lowering. The
   provider rewrites only that emitted escape token and uses an explicit boolean CASE, both verified on the pinned
   server without mutating session SQL mode.
7. This task targets MySQL only. MariaDB compatibility is deliberately not claimed without running the same suite
   unchanged against MariaDB.

Notes: The connector preserves the Entity API, supports discovered and named-source placement, executes filters and
stable paging natively, guards managed-scope writes/deletes, uses InnoDB transactions for atomic batches, and rejects
denied DDL, cross-database inspection, or incompatible schemas at the connector boundary. The full solution compiled
with 0 warnings. Package quality reports 97 packages, 0 repair, 10 review, and 87 structurally ready.

### T4 — Mongo Atlas Vector — Done — 2026-08-19

Commit: this commit (`feat(connector): mongo atlas vector search on the vector plane`)
Oracle: `pwsh scripts/forge-verify.ps1 -Adapter MongoAtlasVector -Plane vector` -> exit 0 (28 passed, 0 failed, 0 skipped)
Acceptance: skills-verify pass · docs-lint Errors: 0 · build pass with 0 warnings · discoverability done · package quality regenerated

Deviations:
1. The task did not pin an executable Atlas image. The test-local fixture uses the official Atlas Local image at
   immutable digest `sha256:3597ce32156af585890ddb4b08d0484f33d596d7ae9140a62199872185d91c41` and proves native
   exact search, filtering, asynchronous index readiness, and a real stop/start without cloud credentials.
2. Existing Mongo discovery and client managers are internal to the record connector. Production remains independent
   of that package: MongoAtlasVector reuses the selected Mongo endpoint and service identity through its own bounded
   client manager, but adds no second service, record-connector edit, or autonomous Mongo lifecycle.
3. Vector collections live in the vector-owned `KoanVectors` database and carry a deterministic `_vector` suffix.
   Shared naming otherwise emits the same Entity anchor, so both boundaries are required for same-service record/vector
   coexistence without collection collision.
4. Native filtering uses one standard Atlas Search index with dynamic keyword mappings and explicit vector mapping,
   then executes `$search.vectorSearch` with `exact: true`. Fixed typed SHA-256 projection tokens preserve exact
   equality/set semantics without rebuilding an index for every metadata path.
5. Atlas Local removes search-index namespaces asynchronously after a database drop. Reusing the same namespace per
   inherited fact caused the next legitimate index build to remain pending for 120 seconds. The disposable fixture now
   gives each fact isolated `KoanVectors_*`/`KoanRecords_*` databases and lets container teardown reclaim them; the
   production default remains exactly `KoanVectors`.
6. Atlas Search reports Euclidean similarity from squared distance (`1/26` for a 3-4-5 displacement). The connector
   converts that native score to Koan's portable higher-is-closer distance normalization (`1/6`) while retaining the
   native score for deterministic tie ordering.
7. The global 15-second V-24 default contradicted Atlas Session visibility, which must wait for mongot after every
   acknowledged save. Two isolated pinned-runtime measurements were 16.13 and 16.25 seconds, so the repaired T4 profile
   pins 25 seconds while retaining all sixteen save/get/search cycles and the 64 MiB allocation ceiling.
8. Only `search-by-meaning.md` currently enumerates interchangeable vector providers. Recipes that intentionally pin a
   single local provider were not broadened merely to advertise another package.

Notes: MongoAtlasVector keeps the ordinary `Vector<TEntity>` API, coalesces on an existing Mongo deployment while
keeping record/vector storage disjoint, validates immutable native shape and exact-filter analyzers, pushes the declared
filter algebra before ranking, performs native bulk work, and provides bounded Session visibility. Ordinary MongoDB
fails correctively because Atlas Search is required. The full solution compiled with 0 warnings. Package quality reports
98 packages, 0 repair, 10 review, and 88 structurally ready; the generated connector matrix reports 33 providers.

### Initiative complete — 2026-08-19

T1 pgvector, T2 Redis vector, T3 MySQL, and T4 Mongo Atlas Vector are all Done. Every literal provider oracle exits 0,
no task is in progress, discoverability/package artifacts are current, and the connector-fleet initiative is complete.
