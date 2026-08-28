# ANL-0 · DuckDB connector spike — probes before promises

> **Tier**: T3 · **Depends on**: — · **Normative decision**: [DATA-0123](../../../decisions/DATA-0123-embedded-analytics-and-duckdb-connector.md)
> Self-contained session prompt — paste into a fresh session. This is a **spike card**: its
> output is recorded probe verdicts, not production code. Update the roster in
> [../README.md](../README.md).

---

## Why this exists

DATA-0123 adopts a DuckDB connector, but every upstream claim about it is *unverified on this
repo's floor*: NativeAOT publishability of the DuckDB.NET ADO.NET layer is unannotated
("works, unverified by CI badge" — the source-generated `[LibraryImport]` P/Invokes are the
right shape, nothing more), extension preloading for air-gapped hosts is untested, and the
single-writer failure mode has never been driven through Koan's health/doctor machinery. The
June 2026 survey set the rule: *treat every native candidate's AOT-compatibility as unverified
until a spike says otherwise.* This card is that spike.

## Mission

In a **scratch console** (not `src/`), reference `DuckDB.NET.Data.Full` and drive the six
probes below. Record each verdict in a new evidence file
`docs/assessment/evidence/duckdb-connector-spike.md` with the command, output, and a stated
verdict (works / works-with-curation / broken). Do not proceed to connector code.

### Probe 1 — NativeAOT publish

`dotnet publish -c Release -r win-x64 -p:PublishAot=true --self-contained true` (the SQLite
leg's hard requirement; expect the same here). Run the binary. Record: publish result, binary
size, warnings count, and whether a real query executes. If ilc refuses, capture the exact
offending construct. Repeat for `linux-x64` if a Linux target is reachable from this machine;
otherwise state win-x64-only coverage and mark linux as unprobed.

### Probe 2 — RID and native payload coverage

Unpack `DuckDB.NET.Bindings.Full` (the version matching current DuckDB stable). List
`runtimes/*/native/*`. Verify the **floor RID set** (`linux-x64`, `linux-arm64`, `win-x64`) is
covered; note `osx-*` and `win-arm64` presence for the record (not floor targets). Record
per-RID native binary sizes.

### Probe 3 — Air-gapped extension preload

With networking disabled (or `autoinstall=false` + empty extension directory), verify: (a) a
Parquet query fails loudly without `parquet`/`httpfs`; (b) pre-placing extension binaries in a
local directory and `LOAD`ing them by path works; (c) `ATTACH '...sqlite'` works with the
sqlite extension preloaded from disk. Record the exact preload steps a connector would own.

### Probe 4 — Dialect smoke against the LINQ plane's expectations

Execute and record results for: `GROUP BY` + `SUM/COUNT`, a window function (`RANK() OVER`),
`RETURNING` (mutation outcomes), `CREATE TABLE` → `ALTER TABLE` (record exactly which ALTERs
fail — feeds the honest `RelationalSchemaPolicy`), named/positional parameters, and `DECIMAL`
round-trips (DuckDB.NET issue #349 territory).

### Probe 5 — Single-writer failure driven through Koan's seams

Two processes opening the same file read-write. Capture the exact exception text. Then, in a
scratch host, register a minimal `DataAdapterHealthContributorBase` probe and `IDataFailureClassifier`
and verify the lock failure classifies as an ownership/dependency conflict (not a transient
retry) and the health probe degrades the source rather than throwing. This validates the
integration points the v1 card will use
(`SqliteHealthContributor`, `DataFailurePolicy.MayRetryIdempotent`).

### Probe 6 — ATTACH-SQLite demo (the showcase)

Create a SQLite file with rows (use `Microsoft.Data.Sqlite`), ATTACH it from DuckDB, and run an
aggregation over the attached table. This is the canonical Koan pairing ("analytics engine on
top of an existing SQLite application database") — capture the working snippet for the
connector README.

## Acceptance evidence

- `docs/assessment/evidence/duckdb-connector-spike.md` exists with six verdicts, commands, and outputs.
- Each verdict is one of `works` / `works-with-curation:<notes>` / `broken:<reason>` — no "probably".
- The roster row in [../README.md](../README.md) records the go/no-go for ANL-1.

## STOP rule

If NativeAOT publish **breaks** and cannot be made to work with curation (rooting, self-contained
flags, linker descriptors), stop after recording everything and return the verdict — DATA-0123's
phase gate says the AOT verdict must be stated honestly before connector work begins. A broken
AOT story does not necessarily kill adoption (GardenCoop-style curation may suffice) but it
must be a recorded fact, not an assumption.
