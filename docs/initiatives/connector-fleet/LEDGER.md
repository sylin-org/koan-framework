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

> **Next task:** T3 — [MySQL / MariaDB](tasks/T3-mysql.md)
> **State:** in progress
> **Last commit touching this initiative:** `a2d6449f2`
>
> Read [BOOTSTRAP.md](BOOTSTRAP.md) before doing anything. Check T3's STOP preconditions first.

Whoever picks this up next: update the three lines above **before** you start, so an interruption
leaves an accurate resume point rather than a stale one.

## Status

| # | Task | State | Commit | Oracle exit |
|---|---|---|---|---|
| T1 | pgvector | BLOCKED | none | not run |
| T2 | Redis vector | BLOCKED | none | not run |
| T3 | MySQL / MariaDB | Not started | — | — |
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
