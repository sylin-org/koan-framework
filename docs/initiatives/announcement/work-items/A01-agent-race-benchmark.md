---
type: GUIDE
domain: framework
title: "A01 - Agent-race benchmark harness"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-08-27
framework_version: v1.0.12
validation:
  date_last_tested: 2026-08-27
  status: reviewed
  scope: benchmark harness work-item specification
---

# A01 — Agent-race benchmark harness

- Tranche: `T0 — Receipts`
- Status: `draft`
- Depends on: none
- Unlocks: A02, A03
- Owner: maintainer

## Meaningful outcome

`evals/agent-race/` exists as a reproducible harness that measures how fast a coding agent
produces a fixed outcome on Koan versus plain ASP.NET Core: identical prompt, identical judging,
the framework as the only treatment variable. A third party can rerun it from a fresh checkout
with documented commands alone.

## Why now

The initiative's standing rule — the receipt gates the claim — needs its receipt. The
"agent-amplified" thesis (charter claim 3) currently has no measurement; every downstream artifact
cites whatever this harness produces.

## Task definition: "Recipe Box"

The outcome both arms must reach (verbatim in `TASK.md`):

- **Application:** manage cooking recipes; static frontend served at `/` with a recipe list and a
  search box that calls the API.
- **API contract (JSON, exact):** `GET/POST /api/recipes`, `GET/PUT/DELETE /api/recipes/{id}`,
  `GET /api/recipes/search?q=<phrase>`. Entity fields: `id`, `title`, `ingredients` (string
  array), `instructions` (string).
- **Semantic search:** `q` matches by meaning, not keywords; the grader queries with phrases
  sharing no words with any recipe.
- **Constraints:** runs offline on one machine; no API keys; no external services; embedded
  storage; starts with a single command; embeddings from the local model pinned by
  [`docs/recipes/search-by-meaning.md`](../../../docs/recipes/search-by-meaning.md).
- **Non-goals (explicit):** auth, tenancy, pagination, uploads, deployment, UI aesthetics.

Anything beyond the listed surface is optional and unscored. The prompt mentions no framework, no
package name, and no pattern.

## Harness contents

```text
evals/agent-race/
  README.md            experiment design, fairness rules, rerun commands
  TASK.md              the verbatim task prompt given to both arms
  seeds/recipes.json   10 fixed recipes; grader seeds them through the API
  grader/              framework-agnostic checker: HTTP + JSON only
  runs/                one record per run (transcript ref, metrics, grader output)
  REPORT.md            written by A02, not A01
```

### Grader requirements

- Seeds the ten recipes via `POST /api/recipes`, so persistence is proven through the app's own
  contract.
- Runs three **keyword-disjoint probes**: each probe phrase shares zero tokens with the target
  recipe's fields, and the target must rank in the top 3. Probe disjointness is checked
  mechanically (token comparison against the seed corpus) so "semantic" cannot quietly degrade to
  `LIKE`. Candidate probes: "fancy french dinner for guests", "my kid refuses vegetables", third
  designed at execution time with the disjointness check passing.
- Verifies the frontend minimally: `/` serves HTML containing a search input; the page issues a
  request to the search endpoint. Screenshots captured for the report; aesthetics unscored.
- Emits a single machine-readable verdict plus per-check detail.

### Fairness rules (binding)

- Identical `TASK.md`, identical agent, identical model, fresh session per run, no memory of the
  other arm; prompts published verbatim.
- Control arm may use any OSS library; only `Sylin.*` packages are excluded from it.
- Koan arm gets the published `koan` skill and public docs; nothing unpublished.
- At least five runs per arm; medians are the reported statistics (A02).
- Embedding model pinned to the same local model on both arms.
- Arms run on the same machine class; environment recorded per run.

## Evidence to read first

- [`../CHARTER.md`](../CHARTER.md) — the three claims; this harness receipts claim 3.
- [`../../../docs/recipes/search-by-meaning.md`](../../../docs/recipes/search-by-meaning.md) —
  cold-validated Ollama path; pins the embedding model and the offline constraint.
- [`../../../docs/recipes/serve-a-web-frontend.md`](../../../docs/recipes/serve-a-web-frontend.md)
  — the documented static-frontend path the Koan arm may discover.
- [`../../../evals/koan/journeys/README.md`](../../../evals/koan/journeys/README.md) — the
  existing eval layout `runs/` records should resemble.

## Acceptance criteria

- [ ] Harness present as laid out above; `TASK.md` contains no framework vocabulary.
- [ ] Grader passes mechanically against a manually built reference Koan app (the recipe's own
      validated path) and fails clearly against a deliberately keyword-search-only app.
- [ ] Probe disjointness check exists and passes for all three probes against `seeds/recipes.json`.
- [ ] One full smoke run per arm is recorded in `runs/`, including one transcript.
- [ ] `README.md` reruns everything from a fresh checkout with documented commands.
- [ ] ACCEPTANCE gate §2 (reproducibility) and §0 (no claims yet — A01 publishes no numbers) pass.

## Proof

Smoke-run records under `runs/` with grader output, linked from `PROGRESS.md`.
