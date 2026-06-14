# F1 · The 20-line fail-loud boot fix

> **Source**: docs/assessment/06-prompt-stash.md · Track F — fail-loud boot · **Tier**: T2 · **Depends on**: — (run early)
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
TASK: Implement the corrected fail-loud boot policy from
docs/assessment/evidence/stage4/fail-fast.json — READ its refinedRecommendation and implement
EXACTLY that: in src/Koan.Core/Hosting/Bootstrap/AppBootstrapper.cs (initializer loop ~:112-123
and the manifest-invoker catch ~:227-245): write module type + full exception to Console.Error,
record into the registry summary (RegistrySummarySnapshot channel → boot report MODULES-FAILED
block), and rethrow wrapped in a NEW sealed KoanBootException {module, assembly, phase, inner}
UNLESS env var KOAN_BOOT_LENIENT=1. Leave the assembly-closure catches (~:60,72,92,95) lenient
but counted. Do NOT create any other exception types. Mirror the naming/style of
KoanBackgroundServiceOrchestrator.FailFastOnStartupFailure (read it — it is the in-repo
precedent).
ADD TESTS: a throwing fake IKoanInitializer → KoanBootException with module name; with
KOAN_BOOT_LENIENT=1 → host boots + failure visible in the registry summary.
VERIFY: full non-container test run green (this touches every test's boot path — if unexpected
suites fail, the failing module was relying on silent swallow: STOP and report it, that's a
real finding).
```
