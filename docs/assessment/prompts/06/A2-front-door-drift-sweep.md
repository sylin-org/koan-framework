# A2 · Front-door drift sweep (remaining stale docs)

> **Source**: docs/assessment/06-prompt-stash.md · Track A — truth restoration · **Tier**: T2 · **Depends on**: —
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
TASK: Remove ghost APIs and stale version pins from the remaining user-facing docs. README,
docs/index.md, getting-started/*, principles.md, samples/README.md are ALREADY rewritten — do
not touch them. Targets: docs/architecture/comparison.md, docs/getting-started/
enterprise-adoption.md, docs/guides/semantic-pipelines.md (if present), docs/support/
troubleshooting.md, .claude/skills/quickstart/SKILL.md, .claude/skills/debugging/SKILL.md.
RULES:
1. Ghost APIs to remove/replace wherever found (verify each with grep before claiming):
   Flow.OnUpdate / UpdateResult (Flow pillar deleted) · Todo.SemanticSearch as entity static
   (real: EntityEmbeddingExtensions.SemanticSearch<T>(query, ...)) · Koan.Messaging.InMemory
   (no such package) · .Embed(new AiEmbedOptions...) pipeline stage (does not exist; Tokenize
   already embeds) · koan CLI as `koan` (tool is koan-orchestrate, and the stack is condemned
   by ARCH-0077 — remove the recommendation entirely) · AddKoan(options => ...) (no overload) ·
   `--version 0.6.3` pins (never published; remove or replace with "current").
2. In comparison.md: any 🟩 cell resting on Flow or the .Embed stage downgrades to 🟨 with an
   honest note, or the row is removed.
3. In the two skills: fix the Describe(BootReport, ...) sample to the real signature — read
   src/Koan.Core/IKoanAutoRegistrar.cs first and copy the actual method shape.
4. Remove `framework_version: v0.6.3` and false `validation:` stamps from the front-matter of
   every file you touch (do not add new version pins).
VERIFY: scripts/validate-code-examples.ps1 and scripts/docs-lint.ps1 pass on touched files.
STOP IF: a doc's claim seems true but you cannot find the API — report it, don't guess.
```
