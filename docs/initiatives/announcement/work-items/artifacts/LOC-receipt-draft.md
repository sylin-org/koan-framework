# LOC receipt — first cut (A11, provisional)

> Status: **provisional**. Measured 2026-08-28 against the two fully-graded staged-composite
> pairs preserved under `evals/agent-race/matrix/cells/test01-staged-composite/` (cell code is
> untracked run artifact). Both arms of each pair passed the identical 22-check battery —
> CRUD, query-every-field, keyword-disjoint semantic search, restart persistence, health — so
> behavioral equivalence is grader-attested, not asserted. This draft is superseded by the
> committed curated pair when A03's demo app and the stock-guidance plain twin exist (A11's
> acceptance requires both apps committed and runnable; this draft records the numbers early
> because they exist and are measured).

## The table

| Graded pair (identical task contract, both arms 22/22) | Koan app | Plain ASP.NET Core app | Ratio |
|---|---|---|---|
| claude-default | **152 LoC** / 4 code files | **608 LoC** / 20 code files | 4.0× |
| agy-gemini | **74 LoC** / 3 code files | **384 LoC** / 4 code files | 5.2× |

A third pair (codex-sol-high) is countable only on the plain side (318 LoC / 5 files); its koan
code was not preserved under the cell, so it is recorded but not paired.

Auxiliary counts, same rules:

| Pair | Package refs (koan / plain) | Config keys (koan / plain) | Migrations (koan / plain) |
|---|---|---|---|
| claude-default | 7 (`Sylin.Koan.*`) / 3 | 15 / 10 | 0 / 0 |
| agy-gemini | 6 / 1 | 13 / 12 | 0 / 0 |

Both sides start with `dotnet run`; neither arm needed a migration step (each bootstraps its
store at startup — the plain side writes that bootstrapper, the koan side does not). Config
keys sit in the same band on both sides, and the Ollama endpoint appears in both — neither side
is config-penalized or config-favored.

## What the difference is made of (claude pair, counted)

- The koan app is five `.cs` files: a 6-line `Program.cs`, a 46-line `Recipe` (entity +
  `[Embedding]` declaration), a 175-line controller (the full governed REST surface + search
  endpoint), 35 lines of search wiring, the csproj.
- The plain app needed 23 `.cs` files to reach grader parity. The telling block is
  `Embeddings/`: eight files — client interface, Ollama HTTP client, options, embedding
  document, vector math, scored-record and unavailable-exception types — about 204 counted
  lines whose only job is to rebuild what `Sylin.Koan.AI` + the `[Embedding]` attribute own.
  The rest is the conventional stack: DTO contracts, an input validator, `DbContext`, queryable
  helpers, a schema bootstrapper, split endpoint classes, and a 117-line search service.
- Neither arm shipped a `wwwroot` (both served the graded frontend inline), so no static-asset
  lines appear on either side.

## Method (mirrors the A11 card)

- Counted: non-blank, non-comment lines in `.cs` plus served static assets (`.html/.css/.js`;
  none present in these cells).
- Excluded: `obj/`, `bin/`, generated files, `koan.lock.json` and other JSON (config counted
  as keys instead — Ollama endpoint appears on both sides, so neither side is config-penalized).
- Provenance of both arms: frontier coding agents, fresh sessions, identical task contract, no
  framework names in the prompt; the koan arm had the published `koan` skill and public docs,
  which is the framework's onboarding surface and part of the product. The plain arm is an
  agent under plain-ASP.NET guidance, not a human developer — A11's stock-guidance human-curated
  twin supersedes this pair.

## Reproduction

```
python docs/initiatives/announcement/work-items/artifacts/loc-count.py \
  evals/agent-race/matrix/cells/test01-staged-composite/claude-default/koan/code \
  evals/agent-race/matrix/cells/test01-staged-composite/claude-default/plain/code
```

(and likewise for `agy-gemini`). The cell `code/` directories are untracked run artifacts on
the operator machine; once A03's committed pair exists, the same command runs against
committed trees and this draft is retired.
