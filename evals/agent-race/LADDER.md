# Scenario ladder — one domain, one pillar per rung

All scenarios grow the same domain — a recipe journal — so the ladder itself demonstrates the
framework thesis: the application keeps saying `Recipe` while each added package reference grows
what it can do. Every scenario is a standalone isolated run; the task text is cumulative.

Reference solutions exist for every rung as cold-validated recipes under `docs/recipes/`; they are
the grader author's material and are never quoted into the agent's prompt.

| # | Scenario | Pillars added | Task delta | Reference recipe |
|---|---|---|---|---|
| S01 | Store and expose | Data + Web | CRUD REST API for recipes, SQLite persistence, restart-surviving | store-and-expose |
| S02 | Search by meaning | AI + Vector | `GET /api/recipes/search?q=` matching by meaning; keyword-disjoint probe must rank target top-3 | search-by-meaning |
| S03 | Work in background | Work (Jobs) | POST schedules a durable background job that computes `summary` and updates the recipe | run-work-in-background |
| S04 | Tell another system | Communication | recipe-created event published to a named channel; a handler records an audit entry | publish-to-a-named-channel |
| S05 | Let an agent in | Agent (MCP) | MCP surface over recipes; read-only for unauthenticated callers; advertised tools prove the gate | let-an-agent-use-my-app |
| S06 | The composed whole | Web frontend + all | static frontend at `/` with list + search box; full battery across all rungs | poc-an-idea |

## Definitions of success (mechanical, HTTP-only)

- **S01** — build exits 0; app listens on `http://localhost:5099`; POST → 2xx with id; GET lists it;
  PUT changes it; DELETE removes it; after a full app restart the remaining recipe is still there.
- **S02** — S01 battery passes; `POST` then `search?q=<keyword-disjoint phrase>` returns the target
  recipe in the top 3 for all three fixed probes (probes pinned in `graders/probes-s02.json`, each
  mechanically verified to share zero tokens with the seed corpus).
- **S03** — S01 battery passes; POST returns before the summary exists; within 60 seconds the
  recipe's `summary` field is populated; a restart does not lose or duplicate the work.
- **S04** — S01 battery passes; creating a recipe produces exactly one audit entry recording the
  event, observable over HTTP.
- **S05** — S01 battery passes; an MCP client can list and read recipes; mutation tools are absent
  or denied for the unauthenticated caller; advertised tool list is the enforcement surface.
- **S06** — all of the above; `/` serves HTML with a search input; the page issues a request to the
  search endpoint; screenshot captured.

## Scoreboard (filled by runs; medians only after ≥5 per arm)

| Scenario | Arm | Runs | Wall clock (s) | Input tokens | Output tokens | Pass rate |
|---|---|---|---|---|---|---|
| S01 | koan | 1 (canonical, skill v4 one-block) | 279 | 1,359,842 | 9,868 | 7/7 |
| S01 | plain | 1 (canonical) | 203 | 495,697 | 7,299 | 7/7 |
| Staged composite | koan (codex-sol-high) | 1 | 1,759 (787+501+471) | ~3.0–4.6 M | 15,087 | 9/9 → 16/16 → 22/22 |
| Staged composite | plain (codex-sol-high) | 1 | 802 (226+246+330) | ~0.85 M | 9,273 | 9/9 → 16/16 → 22/22 |
| Staged composite | koan (claude-default) | 1 | 1,854 (629+738+487) | cost: $12.37 | — | 9/9 → 16/16 → 22/22 |
| Staged composite | koan (agy-gemini) | 1 | S1 hit 30-min cap; S2 40 s; S3 117 s | n/a | — | 9/9 → 16/16 → 22/22 |
| Staged composite | plain (agy-gemini) | 1 | 259 (93+34+132) | ~195 K/turn cum. | — | 9/9 → 16/16 → 22/22 |
| Staged composite | koan (opencode-qwen35-9b) | 1 | 71 (stage 1) | ~32 K/turn | 97 | **0/9 — agent never engaged tools** |
| Staged composite | plain (opencode-qwen35-9b) | 1 | 218 (stage 1) | — | — | **0/9 — partial scaffold, no build, no running app** |
| Staged composite | koan (opencode-qwen38-27b) | 1 | 634 (stage 1) | — | — | **harness-blocked — tool calls emitted but never executed** |
| Staged composite | koan (codex-oss-qwen38-27b) | 2 attempts | 634 / 665 (stage 1) | — | — | **0/1 ×2 — pipe works; model ends turn before writing** |
| Staged composite | koan (codex-oss-qwen38-27b-max, 100% GPU) | 1 | 2701 (cap) | — | — | **0/1 — sustained loop to cap, researched the real PATCH/PUT seam, wrote nothing** |

Single-run results, not medians; A02 requires ≥5 per arm before any median publishes. The Koan
arm's treatment is the `koan` skill (read-and-follow pointer in the prompt), current at **skill
v4** — the greenfield one-block skeleton added 2026-08-27. Prior skill versions' runs are
archived under `attempts/` and excluded. S01 honest reading: the one-block skeleton halved the
cold-start cost (404→279 s, −44% input tokens vs v3); the control still leads plain CRUD.

**Staged composite honest reading (2026-08-28):** both codex arms passed all 22 checks, including
the three keyword-disjoint semantic probes; the control was faster at every stage and ~4–5×
cheaper in context. On a frontier model (`gpt-5.6-sol` @ high), the crossover hypothesis did not
hold — the control hand-rolled Ollama semantic search in 5.5 minutes. Charter claim 3
(agent-amplified) must not publish on this evidence for frontier tiers.

**Cross-harness readings (single runs):** claude-default (Opus-class) passed all 22 checks in
~30.6 min ($12.37 harness-reported cost); its plain pair is blocked by the operator's monthly
spend cap. agy-gemini passed all 22 checks — stage 1 hit the 30-minute cap mid-self-verification
(state passed anyway), stages 2–3 completed in 40 s / 117 s, consistent with `EntityController<T>`
answering field queries natively and `[Embedding]` being additive; marginal-cost interpretation
waits on transcript analysis. opencode + local qwen35-9b (≤12 GB) produced the strongest low-tier
datapoint yet: the model never engaged its tools — one turn, ~97 output tokens, no files, 0/9 —
at this tier the question is not speed but whether an application exists at all (caveat: an
opencode↔Ollama tool-calling gap cannot be separated from model ceiling in this run).
