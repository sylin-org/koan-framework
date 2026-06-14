# C7 · Migrate Koan.Secrets (Abstractions + Core + Vault) → agyo-tools (Agyo.Secrets)

> **Source**: docs/assessment/06-prompt-stash.md · Track C — the cut waves · **Tier**: T2 · **Depends on**: B1
> **Reorg (2026-06-14)**: migrate (was BLOCKED — consumed downstream) — preserve Secrets for the consumer in agyo-tools, not park; see docs/assessment/08-agyo-reorganization.md + agyo-tools docs/decisions/AGYO-0001.
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

_Reclassified by 08-agyo-reorganization (2026-06-14): MIGRATE/SPLIT — was BLOCKED (consumed downstream), now preserved for the agyo consumer rather than parked._

```text
TASK: migrate Koan.Secrets.Abstractions + Koan.Secrets.Core + src/Connectors/Secrets/Vault
(~640 LOC, 3 projects) from the Koan framework to agyo-tools as Sylin.Agyo.Secrets
(.Abstractions / + .Connector.Vault).
JUSTIFICATION (08-agyo-reorganization): secrets resolution is a useful opt-in helper, not core —
it touches only Koan PUBLIC packages (Sylin.Koan.Core + Sylin.Koan.Orchestration.Abstractions)
and Koan.Data.Core carries ZERO Secrets ProjectReferences, so nothing in core compiles against
it. It was originally tagged BLOCKED because a downstream consumer still resolves secrets at
runtime; that makes it valuable to PRESERVE, not delete — so it migrates to agyo where the
consumer can keep consuming it as a published package. The in-core wiring is reflection-only and
survives the move (see CRITICAL SEAM below).
SOURCE: Koan working tree (still present, not yet attic'd):
  - src/Koan.Secrets.Abstractions/
  - src/Koan.Secrets.Core/
  - src/Connectors/Secrets/Vault/
ENTRY CRITERION (already verified in 08): touches only Koan PUBLIC packages
(Sylin.Koan.Core, Sylin.Koan.Orchestration.Abstractions); no internals / no InternalsVisibleTo.
CRITICAL SEAM — LEAVE IN KOAN EXACTLY AS-IS, DO NOT TOUCH:
  The lone in-core touch is the reflection-only fail-soft probe TryInvokeSecretsBootstrap in
  src/Koan.Data.Core/ServiceCollectionExtensions.cs (~lines 99-109) plus its [DynamicDependency]
  hint (line 99). It does Type.GetType("Koan.Secrets.Core...", throwOnError:false) + catch, so it
  resolves agyo's package when the type name still matches at runtime and no-ops when absent.
  Koan.Data.Core.csproj has zero Secrets ProjectReferences, so nothing compiles against it.
  NOTE on rebrand: the probe looks up the type by the literal string
  "Koan.Secrets.Core.Configuration.SecretResolvingConfigurationExtensions, Koan.Secrets.Core".
  If you rebrand the namespace token in agyo (Koan.Secrets -> Agyo.Secrets), that runtime string
  will NO LONGER match the agyo assembly — record this in 08 / AGYO-0001 as a transition concern
  for the consumer and STOP if the recipe and reality diverge here; do not silently change the
  Koan probe.
STEPS (the proven WebSockets/C2 pattern):
  1. In agyo-tools (F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools): recover the three
     source dirs into src/Secrets/ (Abstractions, Core, Connectors/Vault); rebrand ONLY the token
     Koan.Secrets -> Agyo.Secrets (namespaces + assembly names + publishes Sylin.Agyo.Secrets.*).
     Koan.Core / Koan.Orchestration.Abstractions tokens STAY (consumed via package). Drop each
     project's per-project version.json.
  2. Write the Agyo.Secrets csproj(s): swap ProjectReference -> PackageReference Sylin.Koan.Core +
     Sylin.Koan.Orchestration.Abstractions (versions from
     F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools/local-feed); keep any third-party refs
     (e.g. the Vault/HTTP client deps the Vault connector already carries).
  3. PER-CAPABILITY WORK: none — straight lift; no decouple / finish / KoanModule re-express
     required (it already builds against Koan public packages).
  4. dotnet build + dotnet pack -> Sylin.Agyo.Secrets.Abstractions / Sylin.Agyo.Secrets.Core /
     Sylin.Agyo.Secrets.Connector.Vault; dotnet sln Agyo.sln add the new projects; update agyo
     docs/SURFACES.md with the Secrets row(s).
  5. TEST-CANON (AGYO-0001): port/author at least one spec exercising secret resolution +
     the Vault connector. NOTE: ships UNTESTED today in Koan (zero tests/consumers in-repo was
     the original park rationale) — author the first spec in agyo, do not inherit a green.
  6. TRANSITION SAFETY (consumer-facing): do NOT remove from Koan until agyo publishes
     Sylin.Agyo.Secrets.* AND the downstream consumer re-points (incl. resolving the runtime
     type-name string mismatch noted in CRITICAL SEAM). Keep Koan publishing the old
     Koan.Secrets.* packages until then.
KOAN-SIDE (only AFTER transition is confirmed): remove the three projects from Koan.sln; sweep
  docs/modules-overview.md / module-ledger.md / capability-map.md (grep the project names under
  docs/ and clean/strike each hit). KEEP the TryInvokeSecretsBootstrap probe + its
  [DynamicDependency] in src/Koan.Data.Core/ServiceCollectionExtensions.cs (the seam stays).
VERIFY: agyo build+pack green (Sylin.Agyo.Secrets.*); Koan build green after any removal
  (dotnet build Koan.sln) — and the soft probe still present + compiling.
DONE WHEN: Secrets lives in agyo as Sylin.Agyo.Secrets.* (Abstractions/Core/Connector.Vault),
  layering clean (only Sylin.Koan.* PackageReferences), both repos green, and the Koan seam left
  intact for the transitioning consumer.
```
