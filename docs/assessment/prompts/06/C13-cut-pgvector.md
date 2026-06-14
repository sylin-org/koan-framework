# C13 · Migrate PGVector → agyo-tools (Agyo.Data.Vector.PGVector) + finish

> **Source**: docs/assessment/06-prompt-stash.md · Track C — the cut waves · **Tier**: T2 · **Depends on**: B1
> Self-contained session prompt — paste this entire file into a fresh session.
> Update [PROGRESS.md](../PROGRESS.md): set your row `in-progress` when you start; `done`/`blocked` when you finish.
> **Reorg (2026-06-14)**: migrate + finish (was cut — did not compile) — pgvector search is real value; finish it in agyo — see docs/assessment/08-agyo-reorganization.md + agyo-tools docs/decisions/AGYO-0001.

---

## Session preamble

```text
You are working on the Koan Framework (.NET 10 meta-framework; repo root = the working
directory). Rules for this session — they override your defaults:

1. SCOPE: do exactly the task below. One intent per session. No drive-by fixes, no
   refactoring outside the named files, no "while I'm here".
2. EVIDENCE FIRST: before editing, read the files the task names. Never reference an API you
   have not seen in this session — the repo's older docs contain APIs that do not exist; grep
   before you trust. Any API you use in code or docs must be evidenced by a file:line you read.
3. VERIFY: run the named verification (at minimum: `dotnet build Koan.sln`). A session that
   cannot get back to green REVERTS its changes and reports — never "fix forward" into new scope.
4. OUTPUT CONTRACT: your final summary lists every file touched, and for every claim cites
   evidence ("removed X — verified zero references: grep '<pattern>' = 0 hits"). No vague claims.
5. STOP CONDITIONS — stop and report instead of choosing, if you hit ANY of: a failing test you
   did not expect; an API that does not match this recipe; a second plausible way to do the task;
   a reference to the thing you're removing from a file this recipe did not predict.
6. NO-GO ZONES (do not modify, ever, in a T1/T2 session): src/Koan.Data.Core/Model/**,
   EntityContext internals, src/Koan.Core/Hosting/** (except where a recipe names exact lines),
   RegistrySourceGenerator, capability token definitions, any adapter's query-translation code,
   any public API rename.
7. CONVENTIONS: Newtonsoft.Json is the canonical serializer (do not introduce STJ surfaces).
   Canonical entity verbs: Save / Remove / Query. Canonical module primitive: KoanModule.
   Never manually register framework services. Never add a new Add*() extension where a
   registrar exists. Commit messages: conventional commits (feat/fix/refactor/docs/test/chore).
```

---

## Task

_Reclassified by docs/assessment/08-agyo-reorganization.md from CUT (row C13) to MIGRATE/SPLIT._

```text
TASK: migrate the PGVector connector (~1360 LOC) from the Koan framework to agyo-tools as
Sylin.Agyo.Data.Vector.PGVector. This is a migrate-AND-FINISH: the capability was cut from Koan
because it did not compile (its own csproj comment + out-of-sln + fix parked on branch
trusting-mccarthy); the source survives at tag attic/pgvector. Recover it, rebrand it, and
finish the compile in agyo.

JUSTIFICATION (08-agyo-reorganization): pgvector search is real, useful opt-in value — a Postgres-
native vector store is a legitimate alternative to Qdrant for users already on Postgres. It does
not belong in Koan core (it is a single-provider connector, not a framework primitive), but it is
too valuable to confirm-delete. It touches only Koan PUBLIC packages (Sylin.Koan.Data.Vector +
Sylin.Koan.Data.Abstractions + Sylin.Koan.Core), so it ports cleanly to agyo-tools under the
STACK-0001 layering law (names never flow down; agyo consumes Koan via packages only).

SOURCE: Koan git tag attic/pgvector, path src/Connectors/Data/Vector/PGVector (project was
Koan.Data.Connector.PGVector). NOT in the working tree — recover from the tag.

ENTRY CRITERION (already verified in 08): touches only Koan PUBLIC packages (Sylin.Koan.Data.Vector,
Sylin.Koan.Data.Abstractions, Sylin.Koan.Core) — no internals, no InternalsVisibleTo.

STEPS (the proven WebSockets/C2 pattern):
  1. In agyo-tools (F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools): recover the source
     from Koan tag attic/pgvector (path src/Connectors/Data/Vector/PGVector) into
     src/Data/Vector/PGVector/; rebrand ONLY the token Koan.Data.Vector.PGVector ->
     Agyo.Data.Vector.PGVector (namespaces + assembly name). Koan.Core / Koan.Data.* tokens that
     name CONSUMED packages stay as-is. Drop the per-project version.json.
  2. Write Agyo.Data.Vector.PGVector.csproj: replace each ProjectReference with a PackageReference
     to the Koan public package — Sylin.Koan.Data.Vector + Sylin.Koan.Data.Abstractions +
     Sylin.Koan.Core (versions from
     F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools/local-feed). Keep the third-party
     refs: Npgsql, Pgvector (+ Pgvector.Dapper), Dapper, Newtonsoft.Json.
  3. PER-CAPABILITY WORK (the finish, ~hours — this is why it was cut, fix it now):
       a. DELETE the now-cut interface declaration IVectorFilterTranslator<PGVectorWhere> AND the
          PGVectorWhere record (this generic translator surface was removed from Koan core; it has
          no consumer). KEEP the static PGVectorFilterTranslator.Translate method + the
          FilterSupport capability declaration. Mirror the SHAPE of the live Qdrant connector at
          Koan src/Connectors/Data/Vector/Qdrant — read QdrantFilterTranslator.cs +
          QdrantVectorRepository.cs + Initialization/KoanAutoRegistrar.cs for how the live
          connector expresses its static translator + FilterSupport Caps without the removed
          generic interface.
       b. Re-enable IsPackable (the cut csproj had it off / packaging disabled).
       c. Confirm MaxDimension still compiles after (a) — it referenced types the cut removed.
  4. dotnet build + dotnet pack -> Sylin.Agyo.Data.Vector.PGVector; dotnet sln Agyo.sln add the
     project; update agyo docs/SURFACES.md with the connector's row.
  5. TEST-CANON (AGYO-0001): author at least one integration spec that exercises the connector
     against a REAL pgvector container (mirror the ARCH-0079 integration-spec pattern — real
     discovery host, real container, not a fake). The cut Koan version shipped only an orphan,
     non-compiling test project; do NOT port that — write a fresh green spec.

KOAN-SIDE (not consumer-facing — no Koan-published consumer points at this; do immediately):
  Already cut from Koan.sln at tag attic/pgvector. Sweep docs for any lingering PGVector rows —
  modules-overview.md / module-ledger.md / capability-map.md — and remove/annotate. KEEP the
  generic IVectorFilterTranslator<TNative> deletion that the original C13 cut performed in
  Koan.Data.Vector.Abstractions (its only implementor was PGVector); that deletion stays on the
  Koan side and is precisely why step 3a is needed in agyo.

VERIFY: agyo build + pack green (Sylin.Agyo.Data.Vector.PGVector); the new pgvector integration
spec green vs a real container; Koan build green (already cut, confirm no dangling reference).

DONE WHEN: PGVector lives in agyo as Sylin.Agyo.Data.Vector.PGVector, compiles + packs (the cut
that blocked it is finished), layering clean (only Sylin.Koan.* PackageReferences), at least one
real-container integration spec green, both repos green.
```
