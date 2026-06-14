# C9 · Migrate Koan.Tagging → agyo-tools (Agyo.Tagging)

> **Source**: docs/assessment/06-prompt-stash.md · Track C — the cut waves · **Tier**: T2 · **Depends on**: B1
> Self-contained session prompt — paste this entire file into a fresh session.
> Update [PROGRESS.md](../PROGRESS.md): set your row `in-progress` when you start; `done`/`blocked` when you finish.
> **Reorg (2026-06-14)**: migrate (was ABORTED — consumed downstream) — clean lift to agyo, not park — see docs/assessment/08-agyo-reorganization.md + agyo-tools docs/decisions/AGYO-0001.

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

_Reclassified by 08-agyo-reorganization (was: CUT-TEMPLATE park · row C9). Now a MIGRATE card — clean lift, no split, no seam._

```text
TASK: migrate Koan.Tagging from the Koan framework to agyo-tools as Sylin.Agyo.Tagging.
JUSTIFICATION (08-agyo-reorganization): a distinct app-level helper (TagSet value type with
  public/private scopes + open-ended categories, plus a Tag entity for managed synonym
  registries) that is well-engineered, consumed, and has zero core coupling — it is NOT part of
  the data·web·cache·jobs·mcp·auth·storage core, so it does not belong in Koan; but it IS used by
  the downstream consumer, so it must not be deleted. The original "park to /attic" was the false
  cut/attic binary; with agyo-tools it is a clean wholesale lift. Touches only Koan PUBLIC
  packages (Sylin.Koan.Data.Core for Entity<Tag> + [Index]) — no internals.
SOURCE: Koan working tree src/Koan.Tagging (7 src files + the 4-spec unit suite at
  tests/Suites/Tagging/Unit/Koan.Tests.Tagging.Unit; ~290 LOC).
ENTRY CRITERION (already verified in 08): touches only Koan PUBLIC packages, no internals /
  InternalsVisibleTo. The sole inbound ref is ProjectReference -> Koan.Data.Core.
STEPS (the proven WebSockets/C2 pattern):
  1. In agyo-tools (F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools): recover src/Koan.Tagging
     into src/Tagging/; rebrand ONLY the token Koan.Tagging -> Agyo.Tagging (namespaces +
     assembly + root namespace). Koan.Data.Core stays as-is (consumed via package, name never
     flows down). Drop the per-project version.json.
  2. Write Agyo.Tagging.csproj: swap the ProjectReference ..\Koan.Data.Core\Koan.Data.Core.csproj
     for PackageReference Sylin.Koan.Data.Core (version from
     agyo-tools/local-feed). No third-party refs to carry (the csproj declares none beyond the
     framework ref). Keep the existing PackageTags/Description.
  3. PER-CAPABILITY WORK: none — clean lift, no seam to decouple, no KoanModule to re-express.
     Cleanups on the way (doc drift, do these during the lift): strike the `TagScopeJsonConverter`
     <see cref> in TagScope.cs (and any README mention) — that type does not exist; remove the
     dangling "ADR-0018" citation in Tag.cs XML docs.
  4. dotnet build + dotnet pack -> Sylin.Agyo.Tagging; dotnet sln Agyo.sln add src/Tagging;
     add/refresh the Tagging row in agyo docs/SURFACES.md.
  5. TEST-CANON (AGYO-0001): port the 4-spec unit suite (TagCategorySpec, TagScopeSpec, TagSetSpec,
     TagSlugSpec) into agyo, rebranded; it already ships tested in Koan — keep that coverage.
  6. TRANSITION SAFETY (consumer-facing): do NOT remove Koan.Tagging from Koan until agyo publishes
     Sylin.Agyo.Tagging AND the downstream consumer re-points off Sylin.Koan.Tagging. Keep Koan
     publishing the old Sylin.Koan.Tagging package until then (the cutover must be non-breaking).
KOAN-SIDE (after the consumer re-points — NOT in this card's first pass): remove Koan.Tagging +
  its test suite from Koan.sln; delete the source directories; sweep docs modules-overview.md /
  module-ledger.md / capability-map.md (grep the project name under docs/ and clean each hit; big
  docs get a "migrated to agyo-tools 2026-06" strike-through note). No seam stays in Koan.
VERIFY: agyo dotnet build + dotnet pack green (Sylin.Agyo.Tagging produced against local-feed);
  Koan dotnet build Koan.sln + dotnet test Koan.sln (non-container) green after any Koan-side removal.
DONE WHEN: capability lives in agyo as Sylin.Agyo.Tagging, layering clean (only Sylin.Koan.*
  PackageReferences — Sylin.Koan.Data.Core), both repos green, downstream consumer re-pointed.
```
