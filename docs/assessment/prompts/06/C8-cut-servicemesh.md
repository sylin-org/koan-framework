# C8 · Split — delete Koan.ServiceMesh, migrate Translation → agyo-tools (Agyo.Translation)

> **Source**: docs/assessment/06-prompt-stash.md · Track C — the cut waves · **Tier**: T2 · **Depends on**: B1
> **Reorg (2026-06-14)**: SPLIT (mesh already cut) — ServiceMesh stays deleted; Translation gains a home in agyo, decoupled — see docs/assessment/08-agyo-reorganization.md + agyo-tools docs/decisions/AGYO-0001.
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

_Instantiated from MIGRATE/SPLIT-TEMPLATE · row C8 (reorg 2026-06-14, supersedes the original CUT-TEMPLATE instantiation)._

```text
SPLIT card — two parts. PART A is the original cut (delete the mesh), ALREADY DONE.
PART B is the new migrate (Translation library -> agyo, decoupled from the mesh).

─────────────────────────────────────────────────────────────────────────────
PART A — DELETE (STATUS: complete, see commit f7d8a499)
─────────────────────────────────────────────────────────────────────────────
This is the canonical record. Nothing to re-run; do NOT resurrect any of it.

CUT: Koan.ServiceMesh + Koan.ServiceMesh.Abstractions + the Koan.Service.Translation.Container host.
JUSTIFICATION (08-agyo-reorganization): experimental, no ADR, no tests, container-hostile
  (UDP-multicast service discovery — hostile to containerized/cloud deploy). The return path for a
  mesh is a FUTURE trust-fabric / ZenGarden ADR redesign, NOT a port of this code. Stays cut.
DONE (commit f7d8a499 "refactor: cut Koan.ServiceMesh + Translation, excise Web.Admin defensive surface"):
  - Removed Koan.ServiceMesh + Koan.ServiceMesh.Abstractions + Translation.Container; -66 lines from Koan.sln.
  - Excised the Koan.Web.Admin defensive ServiceMesh surface (KoanAdminServiceMeshSurfaceFactory +
    KoanAdminServiceMeshSurface + the GetServiceMesh action + the ProjectReference); other Admin
    surfaces (GetStatus / GetManifest / GetHealth) intact; build 0/0.
  - Parked broken S8.PolyglotShop sample to attic/.
  - Prechecks cited: inbound refs were only-each-other + Web.Admin (defensive) + S8 (broken);
    the KoanServiceAttribute in Web.Auth / Orchestration are unrelated distinct types; no
    downstream consumer (external-audit: none). Build green; zero live refs.
KOAN keeps NO mesh seam. Do not re-add one here.

─────────────────────────────────────────────────────────────────────────────
PART B — MIGRATE (the new work)
─────────────────────────────────────────────────────────────────────────────
TASK: migrate the Koan.Service.Translation LIBRARY from the Koan framework to agyo-tools as
  Sylin.Agyo.Translation, DECOUPLED from the (now-deleted) service mesh. Library only — NOT the
  .Container host (Part A deleted it; it is not coming back).
JUSTIFICATION (08-agyo-reorganization): the translation logic itself is a useful opt-in helper —
  AI-powered translate / detect-language / chunked-translate over Koan.AI — with no architectural
  dependency on the mesh once the [KoanService]/[KoanCapability]/ServiceExecutor scaffolding is
  stripped. Valuable to keep, but not framework-core (it is a "PowerToys for Koan" tool, not a
  pillar). Not deleted because the translation code is sound; only its mesh wiring was the problem.
SOURCE: Koan git ref f7d8a499~1, path src/Services/Translation/Koan.Service.Translation
  (the LIB only — Translation.cs facade, TranslationService.cs, Models/TranslationOptions.cs,
  Models/TranslationResult.cs, csproj — NOT the sibling Koan.Service.Translation.Container host).
ENTRY CRITERION (verified in 08): touches only Koan PUBLIC packages — its real dependency is
  Koan.AI (Client.Chat / ChatOptions, via Koan.AI.Contracts.Options) plus AppHost from
  Koan.Core.Hosting.App. No internals, no InternalsVisibleTo. The only non-public coupling is to
  Koan.ServiceMesh.Abstractions (the [KoanService]/[KoanCapability] attrs + ServiceExecutor) — and
  that is exactly what Part B strips out.
STEPS (the proven WebSockets/C2 pattern):
  1. In agyo-tools (F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools): recover the LIB source
     into src/Translation/; rebrand ONLY the token Koan.Service.Translation -> Agyo.Translation
     (namespaces + assembly). Koan.AI / Koan.AI.Contracts / Koan.Core stay (consumed via package).
     Drop the per-project version.json. Discard the orchestration-generator opt-in
     (KoanRequiresOrchestrationGenerator) — it existed only for the mesh manifest.
  2. Write Agyo.Translation.csproj: replace the three ProjectReferences with PackageReferences to
     Sylin.Koan.AI + Sylin.Koan.AI.Contracts + Sylin.Koan.Core (versions from agyo local-feed:
     F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools/local-feed). DROP the
     Koan.ServiceMesh / Koan.ServiceMesh.Abstractions references entirely (deleted; replaced by the
     decouple in step 3). No third-party refs needed.
  3. PER-CAPABILITY WORK — DECOUPLE (this is the substance of Part B):
     a. Strip the [KoanService(...)] attribute from TranslationService and the [KoanCapability(...)]
        attributes from its methods (translate / get-languages / detect-language). They are
        ServiceMesh registration metadata with no runtime meaning once the mesh is gone.
     b. Delete the ServiceExecutor<TranslationService> indirection. In Translation.cs the static
        facade currently resolves AppHost.Current.GetService<ServiceExecutor<TranslationService>>()
        and calls Executor.ExecuteAsync<TranslationResult>("translate", options, policy, ct).
        Rewrite it to resolve TranslationService directly (AppHost.Current.GetService<TranslationService>()
        — or a small KoanModule that registers it — your choice; AppHost stays, it is Koan.Core public)
        and call the method directly: Translate(options, ct) / DetectLanguage(text, ct). Drop the
        LoadBalancingPolicy parameter (it was a mesh concept; in-process has one instance).
     c. TranslationService's body is unchanged — it already calls Client.Chat / ChatOptions from
        Koan.AI directly. The Models (TranslationOptions / TranslationResult / LanguageDetectionResult
        / SupportedLanguage) are plain POCOs; carry them over as-is.
  4. dotnet build + dotnet pack -> Sylin.Agyo.Translation; dotnet sln Agyo.sln add; update agyo
     docs/SURFACES.md with the new row (rotation contract: Last exercised = today, Guard = the spec).
  5. TEST-CANON (AGYO-0001): the lib shipped UNTESTED in Koan (no specs — part of why it was cut).
     Author at least one spec in agyo: a TranslationService unit/integration test against a fake or
     real Koan.AI Client (translate happy-path + detect-language). This is the first real guard the
     capability has ever had — do not skip it.
KOAN-SIDE: nothing further. Part A already removed everything from Koan.sln and swept the docs;
  Koan keeps NO Translation and NO mesh seam. (consumer-facing = NO, so no transition-safety hold:
  there is no downstream Koan consumer to re-point — S8 was a broken sample, parked in Part A.)
VERIFY: agyo build+pack green (Sylin.Agyo.Translation, only Sylin.Koan.* PackageReferences); Koan
  build already green at f7d8a499 (Part A).
DONE WHEN: Translation lives in agyo as Sylin.Agyo.Translation, decoupled from the deleted mesh,
  layering clean (only Sylin.Koan.* PackageReferences, zero ServiceMesh tokens), agyo green with at
  least one spec; ServiceMesh stays deleted from Koan.
```
