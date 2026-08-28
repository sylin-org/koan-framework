# DUCK-1 · DuckDB capability delight pass — efficiency, declared superpowers, honest hygiene

> **Tier**: T3 · **Depends on**: ANL-1 (connector), ANL-5/ANL-6 (analytics doors whose hot paths this tunes)
> Self-contained session prompt — paste into a fresh session. Update
> [../README.md](../README.md) when done.

---

## Why this exists

The analytics pillar took three delight passes; the connector got one, at birth. Running the
analytics workloads through it exposed exactly where the rough edges are: the hottest write loop
bypasses the engine's own bulk-ingest path, one connection surface retained SQLite pooling
semantics the entity path deliberately drops, and DuckDB's genuine differentiators (files as
tables, extensions, read-only posture) work but are undeclared — which in Koan means they
effectively don't exist.

## Mission

### 1. Refresh writes through the engine's Appender (with an honest fallback)

Projection refresh is the hottest write loop in the pillar. Replace the chunked multi-row INSERT
in `DuckDbAnalyticsProjectionSink.WriteRowsAsync` with a **staging swap**: append into a
`{table}_staging` sibling through the engine's Appender, then move it into the live table inside
the same transaction as the refresh stamp and ledger (`DELETE` + `INSERT ... SELECT` + drop the
staging after commit). Atomicity is preserved exactly. The Appender cannot express every storage
type (TIMESTAMPTZ, BLOB) — columns outside the supported set fall back to the existing chunked
path, loudly and automatically. The entity **batch** contract keeps its per-item outcomes; the
Appender belongs only where outcomes were never promised.

### 2. One connection-key discipline

The entity path strips `Pooling`/`Cache` keys by design — engine instances are shared per path and
pooling only adds a second physical-connection layer that raced the catalog (the ANL-6 lesson).
The analytics sink built its connections from the raw options string and kept that race. Strip the
same local keys there, through a shared normalizer, and document the rule: **pooling keys are
dropped, not honored**.

### 3. Extensions as declared config

`AutoInstallExtensions` and `ExtensionDirectory` exist; the missing piece is an explicit
allow-list: `Koan:Data:DuckDb:Extensions: sqlite, httpfs`. Declared extensions are `LOAD`ed once
per engine instance (they persist in the instance, so a short-lived connection suffices);
an extension that cannot load refuses with a corrective naming it and pointing at
pre-install vs. autoinstall. The air-gap posture (autoinstall off by default) is unchanged.

### 4. Files as tables — the declared superpower

DuckDB reads Parquet and CSV natively — globs, hive partitioning, schema sniffing — and the
connector exposes it today only through raw SQL lanes that nobody can discover. Make it
**declared**: conformance specs pin glob reads, hive-partition pruning, and CSV sniffing through
the direct lane; the engine guide gains a "files as tables" section; the capability map carries
the row. No new API — the delight is that it becomes part of the contract, not a party trick.

### 5. Read-only posture, stated

`Mode=ReadOnly` translates to the engine's `access_mode=read_only` (a distinct, correctly-forked
engine instance). A read-only open of a missing file refuses (read-only never creates); a
read-only open of an existing store reads and refuses writes. Health/inspector wording treats
"locked by a writer" as its own condition, not generic unavailability.

### 6. File hygiene as evidence, not promise

With pooling keys out of the materialization path, DuckDB auto-checkpoints on the last close.
Prove it: a spec asserts a host that wrote and stopped leaves **no WAL file behind** — so
"back up the app = copy the file" is literally true. If the evidence contradicts, add a
checkpoint-on-stop service; the evidence decides.

### Deliberately deferred

`Koan:DuckDb:Home` (one root for the record store and the derived store): defaults are already
right and the two keys are documented — a unifying knob would add a config surface before the
pain exists. Recorded here as the rejected-for-now candidate.

## Acceptance

- Refresh write path exercised through both the Appender swap and the fallback (a TIMESTAMPTZ
  column forces the fallback); atomicity specs unchanged and green.
- Pooling keys provably inert on both connection surfaces.
- Extension allow-list: built-in (`sqlite`) loads and ATTACH works; a bogus name refuses with a
  corrective naming it.
- File-read specs: glob count, hive partition read, CSV sniff — all through the direct lane.
- Read-only: missing file refuses; existing store reads and refuses writes.
- WAL-after-stop evidence recorded; connector + analytics suites green.
