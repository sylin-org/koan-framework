# D2 · Delete the orchestration stack's dead surface

> **Source**: docs/assessment/06-prompt-stash.md · Track D — orchestration · **Tier**: T2 · **Depends on**: —
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

```text
TASK: Within src/Koan.Orchestration.* (the ARCH-0077-condemned stack), delete only the
verified-dead surface — NOT the CLI/planner/providers themselves:
1. IServiceAdapter, IKoanService, IDevServiceDescriptor in Orchestration.Abstractions (zero
   implementors repo-wide — verify with grep) + the Koan0049A diagnostic in
   Koan.Orchestration.Generators that enforces them.
2. The five legacy attributes [KoanService] superseded (ContainerDefaults, AppEnvDefaults,
   EndpointDefaults, HealthDefaults, ServiceId) — ONLY if grep shows no remaining usage in
   src/ samples/ (the generator still parses them — remove that parsing branch too; read
   OrchestrationManifestGenerator first).
3. The SelfOrchestration subsystem in Koan.Orchestration.Aspire (KoanSelfOrchestrationService,
   KoanDependencyOrchestrator, DockerContainerManager, TestHostEnvironment.cs,
   SelfOrchestrationConfigurationProvider) — verify no sample/appsettings activates
   OrchestrationMode=SelfOrchestrate first; if one does, STOP.
4. Vestigial Aspire ProjectReferences in PGVector (if not already cut) and
   Koan.Service.KoanContext (zero code usage — verify).
5. Tombstone InternalsVisibleTo in Cli/Compose ("...Tests" assemblies that don't exist).
6. Merge Koan.Orchestration.Cli.Core into Koan.Orchestration.Cli (all-internal,
   InternalsVisibleTo only the Cli — verify, then collapse).
VERIFY: build green. STOP IF any [KoanService]-annotated connector fails to compile.
```
