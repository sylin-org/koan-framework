# H8 · Skills refresh + koan-jobs skill

> **Source**: docs/assessment/06-prompt-stash.md · Track H + §8 — DX system · **Tier**: T2 · **Depends on**: H2
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
TASK: 1) Create .claude/skills/koan-jobs/SKILL.md modeled on koan-caching's structure: the
IKoanJob<TSelf> pattern (copy the verified BenchmarkJob shape from
samples/S14.AdapterBench/Jobs/BenchmarkJob.cs), the .Job/.Jobs accessors, the six attributes,
JobContext verbs, the capability ladder, §17 write-safety, the §19 conveyor rule — source:
CLAUDE.md's Background Jobs section + docs/guides/jobs-howto.md. 2) Add the skill to the
pattern-recognition table in CLAUDE.md and .claude/skills/README.md. 3) Sweep all skills for
`0.6.3` version pins and stale package ids (Koan.AI.Ollama → Koan.AI.Connector.Ollama etc.) —
fix against reality (grep the csproj names).
VERIFY: every snippet in touched skills passes H2's lint.
```
