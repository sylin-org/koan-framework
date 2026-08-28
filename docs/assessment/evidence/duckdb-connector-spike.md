# DuckDB connector spike — ANL-0 probe results (2026-08-27)

> **Card**: [ANL-0](../../initiatives/analytics/cards/duckdb-connector-spike.md) · **Decision**: [DATA-0123](../../decisions/DATA-0123-embedded-analytics-and-duckdb-connector.md)
> Scratch console (`DuckDB.NET.Data.Full` **1.5.5** + `Microsoft.Data.Sqlite` 10.0.10, net10.0),
> run both inside a trivial csproj and as a standalone project. Machine: Windows x64,
> .NET SDK 10.0.302. **Overall verdict: GO for ANL-1.**

| # | Probe | Verdict |
|---|---|---|
| 1 | NativeAOT publish + run | **works** (win-x64; linux unprobed on this machine) |
| 2 | RID/native coverage | **works** — all floor RIDs present |
| 3 | Air-gapped extensions | **works-with-curation** — `autoinstall=false` + preloaded local extension dir |
| 4 | Dialect smoke | **works** — full analytic SQL; ALTER limited; positional params only |
| 5 | Cross-process file lock | **recorded** — one process total; even read-only open excluded |
| 6 | ATTACH-SQLite | **works** — scanner is built into libduckdb; cross-store JOIN correct |

## Probe 1 — NativeAOT publish and run

`dotnet publish -c Release -r win-x64 -p:PublishAot=true --self-contained true` on the trivial
console: **succeeded**. Published native binary is **4.4 MB** and *runs every probe*: GROUP BY
aggregates, window functions, `RETURNING`, DECIMAL round-trips, ATTACH-SQLite cross-store
joins — identical output to the JIT build.

Warnings (expected, matching the survey's "unannotated" finding): `IL2104` (trim warnings) and
`IL3053` (AOT analysis warnings) from `DuckDB.NET.Data`. **First measurement contradicting the
survey's pessimism** (survey §9: "unverified, leaning unproven" for AOT): the 1.5.5
source-generated `[LibraryImport]` layer compiles and runs under ILC on win-x64. Linux AOT
remains unprobed (no Linux toolchain on this machine) — the S2-style publish-and-run on
linux-arm64 stays open for CI.

## Probe 2 — RID/native payload coverage

Uncompressed on-disk sizes of `runtimes/<rid>/native` in `duckdb.net.bindings.full` 1.5.5:

| RID | native payload |
|---|---|
| win-x64 | 36.7 MB |
| win-arm64 | 42.8 MB |
| linux-x64 | 70.5 MB |
| linux-arm64 | 63.0 MB |
| osx (universal) | 117.0 MB |

Floor RID set (linux-x64, linux-arm64, win-x64) **fully covered**. Total ~330 MB uncompressed
across five RIDs (the nupkg is compressed — the research's "~105 MB" figure was the compressed
artifact). Split packaging (managed connector + native rider) is confirmed as the right call.

## Probe 3 — Air-gapped extensions

With `SET autoinstall_known_extensions=false; SET autoload_known_extensions=false`:

- `LOAD httpfs` fails with a **clean corrective error** naming the remedy: *"Extension
  'httpfs' is an existing extension. Install it first using 'INSTALL httpfs'"* — exactly the
  fail-loud shape Koan adapters surface.
- `INSTALL httpfs` (network, once) populates `~/.duckdb/extensions/v1.5.5/windows_amd64/`;
  subsequent `LOAD httpfs` succeeds **with autoinstall still disabled** — i.e., a preloaded
  extension directory serves fully offline loads.
- Connector design confirmed: default `autoinstall=false`; options for an extension directory;
  docs own the "pre-install, then LOAD" story.

## Probe 4 — Dialect smoke (in-memory, v1.5.5)

| Capability | Result |
|---|---|
| `GROUP BY` + `COUNT/SUM` | OK (correct DECIMAL totals) |
| Window function `RANK() OVER (PARTITION BY …)` | OK |
| `INSERT … RETURNING id` | **OK** — `write.mutationOutcomes` is real |
| `ALTER TABLE … ADD COLUMN` | OK |
| `ALTER TABLE … RENAME COLUMN` | OK |
| `ALTER TABLE … DROP COLUMN` | OK |
| `ALTER TABLE … ALTER column TYPE` | **OK** (better than the EF-provider lore suggested) |
| `ALTER TABLE … SET NOT NULL` | OK |
| `ALTER TABLE … ADD CONSTRAINT (FK)` | **FAILED** — "No support for that ALTER TABLE option yet!" |
| `DECIMAL(18,2)` round-trip | exact |
| Positional parameter `?` | OK |
| Named parameter `@region` | **FAILED** — DuckDB binds `@…` as a unary operator; the ADO.NET provider does not rewrite named parameters |

**Dialect consequences for ANL-1:** generate SQL with **positional `?` parameters only**;
schema policy can be create + validate + richer repair than assumed (most ALTERs work), with
constraint changes excluded; `RETURNING` backs mutation outcomes.

## Probe 5 — Cross-process file lock (Windows, v1.5.5)

A child process holding a file read-write (deterministic: parent waits for the child's
`LOCK-HELD` signal):

- Second **writer**: fails at open — `IO Error: Cannot open file "…": The process cannot
  access the file because it is being used by another process.`
- **Read-only attach while a writer holds the file: also fails** with the same error — on
  Windows, a read-write holder excludes even `access_mode=READ_ONLY` openers. (Stricter than
  the "many readers + one writer" doc phrasing implies; Linux behavior unprobed on this
  machine.)

**Consequences for ANL-1:** the failure text is a raw OS message (no "conflict"/"lock"
keyword contract) — the `IDataFailureClassifier` must classify on the
`Cannot open file … used by another process` pattern as an ownership/dependency conflict,
non-retryable; health probes degrade the source. Election must guarantee one read-write owner
per file per host; per-tenant files (Database isolation mode) remain the supported multi-tenant
posture.

## Probe 6 — ATTACH-SQLite cross-store aggregation

- `ATTACH '<path>.db' AS app (TYPE sqlite)` **succeeded without any explicit INSTALL** — the
  sqlite scanner is statically built into the bundled libduckdb.
- Reading attached tables and **joining them against native DuckDB tables** produced correct
  aggregates (`SELECT cu.name, SUM(o.amount) FROM duck_orders o JOIN app.customers cu …`).
- Probe artifact, not a defect: the attached file's handle stays open for the connection's
  lifetime (the probe's `File.Delete` failed while attached — expected).

This is the canonical Koan pairing working end-to-end: DuckDB aggregating over the app's
existing SQLite store.

## Adjacent finding (action for the SQLite legs)

The spike's `Microsoft.Data.Sqlite` 10.0.10 pulled `SQLitePCLRaw.lib.e_sqlite3` **2.1.11**,
flagged `NU1903` — **known high-severity vulnerability** (GHSA-2m69-gcr7-jv3q). The repo's
SQLite data/cache legs pin this package; an audit and bump belongs to the next SQLite-touching
card (survey already carried the "deprecated package ID" debt note for the same pin).


## Linux verification (2026-08-28, container probe)

Ran inside `mcr.microsoft.com/dotnet/sdk:10.0` (Debian) against the same spike source:

- **NativeAOT on linux-x64: works.** `dotnet publish -c Release -r linux-x64 -p:PublishAot=true`
  after `apt-get install clang zlib1g-dev` produced a **4.8 MB** native binary that runs the full
  probe battery — aggregates, `RETURNING`, ATTACH-SQLite joins, offline-extension loads. Same
  expected IL2104/IL3053 warnings as Windows. This closes the survey's "Linux AOT unprobed" gap for
  the connector's own publish story (the full Koan-host AOT publish inside Linux remains a CI task).
- **Lock semantics on Linux: same one-writer posture as Windows.** A read-write holder excludes
  both a second writer AND a read-only opener; the failure names the conflicting PID
  ("Conflicting lock is held in ... (PID n)"). The failure classifier's ownership-conflict mapping
  is therefore platform-uniform.
- **Re-acquire after release: works** — a fresh read-write open succeeds once the holder exits.
- Runtime (JIT) probes: identical verdicts to Windows.

## Linux lock probe detail

| Case | Result |
|---|---|
| Second writer while writer holds | Rejected — "Could not set lock on file ... Conflicting lock is held in ... (PID n)" |
| Read-only open while writer holds | Rejected — same conflict error (stricter than the docs' many-readers phrasing implies) |
| Fresh read-write after holder exits | Works |

## Roster outcome

**ANL-0: done — GO.** Linux verification completed 2026-08-28 (see Linux section above): AOT
publishes and runs on linux-x64, lock semantics confirmed platform-uniform.
