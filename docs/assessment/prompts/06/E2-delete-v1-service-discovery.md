# E2 · Delete V1 service discovery

> **Source**: docs/assessment/06-prompt-stash.md · Track E — finish the in-flight migrations · **Tier**: T2 · **Depends on**: — (soft: C7)
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
TASK: Remove the legacy OrchestrationAwareServiceDiscovery (V1) from Koan.Core and migrate its
4 remaining call sites. Evidence: 01-cartography (core-bootstrap redundancies).
RECIPE: grep `new OrchestrationAwareServiceDiscovery` — expect 4 hits in the RabbitMq (×2) and
Vault (×2) connectors. Read how V2 (OrchestrationAwareServiceDiscoveryV2 +
IServiceDiscoveryCoordinator, DI-registered) is consumed elsewhere; convert the 4 sites to
resolve the coordinator from DI (constructor injection in those registrars' service factories).
Then delete V1 + IOrchestrationAwareConnectionResolver (verify zero remaining refs) and rename
V2 to drop the suffix (update its registrations; this is an internal type — verify with grep
that no public API leaks the name; if it does, STOP).
VERIFY: build green; Messaging + Secrets-related bootstrap specs green.
NOTE: if C7 (Secrets park) already landed, the Vault sites are gone — adjust expectations.
```
