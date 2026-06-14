# C5 · Split Koan.Recipe — delete Abstractions, migrate Observability bundle → agyo-tools (Agyo.Observability)

> **Source**: docs/assessment/06-prompt-stash.md · Track C — the cut waves · **Tier**: T2 · **Depends on**: B1
> **Reorg (2026-06-14)**: SPLIT (was reverted) — delete the superseded bootstrap idiom; migrate the observability bundle as a KoanModule — see docs/assessment/08-agyo-reorganization.md + agyo-tools docs/decisions/AGYO-0001.
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

> **Reorg disposition: SPLIT (08-agyo-reorganization).** Koan.Recipe is two halves with opposite fates. **PART A — DELETE** `Koan.Recipe.Abstractions`: the superseded bootstrap idiom (ARCH-0046 → ARCH-0086 KoanModule), built on the `AppDomain.GetAssemblies()` scan anti-pattern, with zero consumers besides the one recipe. KoanModule already owns register/bootstrap/self-report, so Koan keeps **NO seam** — a plain delete. **PART B — MIGRATE** the `ObservabilityRecipe` bundle to agyo-tools as `Sylin.Agyo.Observability`, re-expressed as a `KoanModule` (not `IKoanRecipe`). This was the one externally-broken cut this session (reverted, commit 35318300), so Part B is **consumer-facing** — apply transition safety. **DO NOT** fold the bundle into Koan.Web's registrar (that forces health-checks-always-on into every web app — the exact approach the reverted C5 took). Do Part A as a plain delete; do Part B via the migrate recipe below.

_Instantiated from MIGRATE/SPLIT-TEMPLATE · row C5._

### PART A — delete Koan.Recipe.Abstractions (plain cut, no seam)

```text
TASK: delete the project Koan.Recipe.Abstractions from the Koan framework.
JUSTIFICATION (08-agyo-reorganization): superseded bootstrap idiom (ARCH-0046 → ARCH-0086
  KoanModule) built on the AppDomain.GetAssemblies() scan anti-pattern; zero consumers besides
  the one recipe; KoanModule already owns register/bootstrap/self-report, so Koan keeps NO seam.
PRECHECKS (all must hold, else STOP):
  1. grep IKoanRecipe / Koan.Recipe.Abstractions across **/*.csproj + src/ samples/ tests/ —
     inbound references must match only Koan.Recipe.Observability (the one recipe), nothing else.
  2. Confirm packaging/Koan.nuspec + Koan.App.nuspec do NOT list the package.
STEPS:
  1. <MODE=cut>: remove Koan.Recipe.Abstractions + any test project; remove from Koan.sln; delete
     the source directory. (Part B removes the last consumer, so do A after B's migrate lands, or
     together in the same transition-safe window.)
  2. Sweep docs: modules-overview.md, module-ledger.md, capability-map.md — grep the project name
     under docs/ and clean each hit (big docs: add a "removed 2026-06" strike-through note).
  3. Annotate ARCH-0046 (Superseded by ARCH-0086) if not already marked.
VERIFY: dotnet build Koan.sln green; dotnet test Koan.sln (non-container) green.
DONE WHEN: Koan.Recipe.Abstractions gone, no IKoanRecipe seam left in Koan, docs swept, build+tests green.
```

### PART B — migrate the Observability bundle → agyo-tools

```text
TASK: migrate the Recipe observability bundle from the Koan framework to agyo-tools as
  Sylin.Agyo.Observability, re-expressed as a KoanModule (not IKoanRecipe).
JUSTIFICATION (08-agyo-reorganization): useful opt-in app helper, not core — it wires
  AddHealthChecks + a resilient "Koan-observability" HttpClient. Consumed (this was the one
  externally-broken cut, reverted 35318300), valuable, and touches only Koan public packages. It
  does NOT belong folded into Koan.Web's registrar (that forces health-checks-always-on into every
  web app — the reverted C5 approach). The advertised OTel was NEVER implemented.
SOURCE: Koan working tree — src/Koan.Recipe.Observability (ObservabilityRecipe.cs) + the recipe
  seam in src/Koan.Recipe.Abstractions. Read ObservabilityRecipe.cs first; port exactly what it does.
ENTRY CRITERION (already verified in 08): touches only Koan PUBLIC packages, no internals / no
  InternalsVisibleTo.
STEPS (the proven WebSockets/C2 pattern):
  1. In agyo-tools (F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools): recover source into
     src/Observability/; rebrand ONLY the token Koan.Recipe.Observability -> Agyo.Observability
     (Koan.Core / Koan.Web stay — consumed via package); drop per-project version.json.
  2. Write Agyo.Observability.csproj: ProjectReference -> PackageReference Sylin.Koan.Core +
     Sylin.Koan.Web (versions from agyo-tools/local-feed); keep any third-party refs the bundle
     pulls (none beyond the BCL HttpClient + health-checks today).
  3. PER-CAPABILITY WORK — re-express as a KoanModule: replace IKoanRecipe with a KoanModule whose
     Register(services) wires AddHealthChecks + the resilient "Koan-observability" HttpClient, and
     Report(...) self-describes it. Do NOT carry the AppDomain-scan recipe-discovery machinery
     (that dies with Part A). OTel: it was never implemented — either make it real now (honest
     name) or consume Sylin.Koan.Observability if card G1 extracts it; otherwise ship as
     health-checks + resilient-HttpClient only and say so.
  4. dotnet build + dotnet pack -> Sylin.Agyo.Observability; dotnet sln Agyo.sln add; update
     agyo docs/SURFACES.md row.
  5. TEST-CANON (AGYO-0001): port/author at least one spec exercising the module's
     register + health-check wiring through real AddKoan() discovery (it ships effectively
     untested today — the recipe path had no integration spec).
  6. TRANSITION SAFETY (consumer-facing): do NOT remove Koan.Recipe.Observability from Koan until
     agyo publishes Sylin.Agyo.Observability AND the downstream consumer re-points. Keep Koan
     publishing the old Sylin.Koan.Recipe.Observability package until then.
KOAN-SIDE (after the consumer re-points): remove Koan.Recipe.Observability from Koan.sln; delete
  the source directory; sweep modules-overview.md / module-ledger.md / capability-map.md. Keep NO
  seam (it had none beyond the IKoanRecipe contract Part A deletes). Do NOT fold any of it into
  Koan.Web's registrar.
VERIFY: agyo build+pack green (Sylin.Agyo.Observability); Koan build green after removal.
DONE WHEN: the observability bundle lives in agyo as Sylin.Agyo.Observability (a KoanModule),
  layering clean (only Sylin.Koan.* PackageReferences), both repos green.
```
