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

> **Next task:** None — initiative stopped under BOOTSTRAP's failure protocol
> **State:** T1, T2, and T3 retries are three consecutive BLOCKED tasks
> **Last commit touching this initiative:** `83cd8b64b`
>
> T4 remains not started. Resume only after the task authority conflicts in the retry log are resolved.

Whoever picks this up next: update the three lines above **before** you start, so an interruption
leaves an accurate resume point rather than a stale one.

## Status

| # | Task | State | Commit | Oracle exit |
|---|---|---|---|---|
| T1 | pgvector | BLOCKED | none | not run |
| T2 | Redis vector | BLOCKED | none | not run |
| T3 | MySQL / MariaDB | BLOCKED | none | not run |
| T4 | Mongo Atlas Vector | Not started | — | — |

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
