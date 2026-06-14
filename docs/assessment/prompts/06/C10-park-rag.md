# C10 · Migrate Koan.Rag + Koan.Rag.Abstractions → agyo-tools (Agyo.Rag)

> **Source**: docs/assessment/06-prompt-stash.md · Track C — the cut waves · **Tier**: T2 · **Depends on**: B1
> **Reorg (2026-06-14)**: migrate (was parked to attic) — sophisticated RAG stack with real value; move attic -> agyo — see docs/assessment/08-agyo-reorganization.md + agyo-tools docs/decisions/AGYO-0001.
> Self-contained session prompt — paste this entire file into a fresh session.
> Update [PROGRESS.md](../PROGRESS.md): set your row `in-progress` when you start; `done`/`blocked` when you finish.

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

_Reclassified from CUT-TEMPLATE (park) → AGYO-MIGRATE-TEMPLATE · row C10. The 06-stash justification (8k LOC incubator, zero in-repo consumers, InternalsVisibleTo a nonexistent test project) was correct that this does not belong in Koan core — but the re-evaluation (08-agyo-reorganization) found it is a sophisticated, genuinely valuable RAG stack, so the disposition is MIGRATE to agyo-tools, not park-and-forget. It already lives in `attic/Koan.Rag` + `attic/Koan.Rag.Abstractions`; this card moves that source into agyo as `Sylin.Agyo.Rag` + `Sylin.Agyo.Rag.Abstractions`._

```text
TASK: migrate Koan.Rag + Koan.Rag.Abstractions (~8025 LOC, 2 projects) from the Koan framework
  to agyo-tools as Sylin.Agyo.Rag + Sylin.Agyo.Rag.Abstractions.
JUSTIFICATION (08-agyo-reorganization): a sophisticated RAG stack (chunking, retrieval,
  re-ranking, the UMAP-backed pipeline) with real opt-in value — not core framework surface,
  but too useful to delete. It is a "PowerToys for Koan" helper: pure consumer of Koan's PUBLIC
  packages (AI / Data.Vector / Data.AI / Data.* / Core), no framework internals. Parked to attic
  with zero in-repo consumers, so nothing downstream depends on it staying in Koan; move it where
  it belongs (agyo-tools) instead of leaving 8k LOC dead in the attic.
SOURCE: Koan working tree — attic/Koan.Rag/ + attic/Koan.Rag.Abstractions/ (already recovered to
  the attic; this is the canonical source, no git-ref archaeology needed).
ENTRY CRITERION (already verified in 08): touches only Koan PUBLIC packages (Sylin.Koan.AI,
  Sylin.Koan.AI.Contracts, Sylin.Koan.Data.Vector, Sylin.Koan.Data.AI, Sylin.Koan.Data.Abstractions,
  Sylin.Koan.Data.Core, Sylin.Koan.Core) — no internals; the only InternalsVisibleTo target was the
  never-built Koan.Rag.Tests, which is dropped here.
STEPS (the proven WebSockets/C2 pattern):
  1. In agyo-tools (F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools): recover source into
     src/Agyo.Rag/ and src/Agyo.Rag.Abstractions/ from attic/Koan.Rag + attic/Koan.Rag.Abstractions;
     rebrand ONLY the capability's own token Koan.Rag -> Agyo.Rag (namespaces + assembly names +
     RootNamespace); leave Koan.AI / Koan.Data.* / Koan.Core references untouched (consumed via
     package). Drop each project's per-project version.json (NBGV is repo-root in agyo).
  2. Write Agyo.Rag.csproj + Agyo.Rag.Abstractions.csproj: swap every ProjectReference to a
     PackageReference Sylin.Koan.* (Agyo.Rag refs Sylin.Koan.AI + Sylin.Koan.AI.Contracts +
     Sylin.Koan.Data.Vector + Sylin.Koan.Data.AI + Sylin.Koan.Data.Abstractions + Sylin.Koan.Data.Core
     + Sylin.Koan.Core; versions pinned from agyo-tools/local-feed). Agyo.Rag -> ProjectReference
     Agyo.Rag.Abstractions (sibling, stays a project ref). Keep the third-party UMAP nuget reference.
  3. PER-CAPABILITY WORK (be specific):
       - Drop the `InternalsVisibleTo Koan.Rag.Tests` entry (the test project never existed); a
         real suite is authored under TEST-CANON below.
       - Drop the KoanPackageKind / Koan-specific MSBuild props (Agyo's Directory.Build.props owns
         packaging; do not carry Koan packaging metadata across).
       - Delete any committed bin/ + obj/ build artifacts dragged in from the attic; they must not
         enter agyo source control.
  4. dotnet build + dotnet pack -> Sylin.Agyo.Rag + Sylin.Agyo.Rag.Abstractions (into the local
     feed); dotnet sln Agyo.sln add both projects; add their rows to agyo docs/SURFACES.md.
  5. TEST-CANON (AGYO-0001): port/author at least one real spec exercising the RAG pipeline against
     the Sylin.Koan.* packages. NOTE: this capability ships UNTESTED today (the only ever-named test
     project, Koan.Rag.Tests, was a nonexistent InternalsVisibleTo target) — so it owes its first
     spec as the AGYO-0001 test-canon entry; record that debt explicitly in the SURFACES.md row.
KOAN-SIDE (not consumer-facing — no transition window needed): C10 is already past the Koan cut
  (the projects live in attic/, are out of Koan.sln, and have zero in-repo consumers). After the
  agyo migration lands, finish the Koan side: delete attic/Koan.Rag + attic/Koan.Rag.Abstractions,
  drop their line from attic/README.md, and sweep any lingering Rag mentions in docs
  (modules-overview.md, module-ledger.md, capability-map.md) to point at agyo / mark migrated. The
  Rag Bootstrap integration spec was already removed in the original park; do not re-add it.
VERIFY: agyo build+pack green (Sylin.Agyo.Rag + Sylin.Agyo.Rag.Abstractions resolve only
  Sylin.Koan.* PackageReferences from the local feed); Koan `dotnet build Koan.sln` green after the
  attic deletion (nothing in Koan referenced it, so green is expected — confirm with a grep showing
  0 inbound references before deleting).
DONE WHEN: the RAG stack lives in agyo as Sylin.Agyo.Rag + Sylin.Agyo.Rag.Abstractions, layering
  clean (only Sylin.Koan.* PackageReferences + the UMAP nuget; Agyo.Rag -> Agyo.Rag.Abstractions
  project ref), the AGYO-0001 first-spec debt is recorded in agyo SURFACES.md, the attic copy is
  removed from Koan, and both repos are green.
```
