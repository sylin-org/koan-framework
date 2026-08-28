# Staged composite — the pitch, as an experiment

One session, one application, three stages delivered sequentially. Measures the marginal cost of
growth: what it costs an agent to add a capability to an application it just built. This is the
scenario whose result reads like the announcement claim — "a full REST API; a few more lines, and
it searches by meaning" — so it is the flagship scenario of the race.

## Stage contract

| Stage | Request (verbatim deltas in `stage-N.txt`) | New grader battery | Pillar · capability · validated receipt |
|---|---|---|---|
| 1 | Recipe REST API exercising create, get, update, remove, check (health); SQLite persistence; restart-surviving | 9 checks: build, start, health, create, list, get-by-id, update, delete, persistence | Data + Web · store and expose · `docs/recipes/store-and-expose.md` |
| 2 | Query every field: `?title=&ingredient=&instructions=` case-insensitive contains, AND-combined | +7: case-insensitivity, array-member match, instructions match, AND combine, AND exclude, no-param passthrough | Data · entity query surface · capability docs `data/entities.md` |
| 3 | Semantic search: `GET /api/recipes/search?q=`, local Ollama `nomic-embed-text`, no cloud | +6: three keyword-disjoint probes (disjointness + rank), target in top 3 | AI + Vector · search by meaning · `docs/recipes/search-by-meaning.md` (cold-validated) |

Every stage's grade **accumulates** the previous battery, so regressions are caught: stage 3 runs
all 22 checks. A stage gate fails the run — the per-stage cost is only meaningful on a passing
base.

## Mechanics

- One Codex session per run: stage 1 via `codex exec`, stages 2–3 via `codex exec resume`.
- 30-minute cap per stage (in-prompt and `timeout 1800`), unattended contract in every stage body.
- Per-stage wall clock and token deltas measured at the session boundaries.
- Arm difference is only the arm line prepended to stage 1: Koan (skill pointer) vs plain
  (no `Sylin.*`). Stage bodies are byte-identical across arms and contain no implementation hints.
- Control arm runs in a neutral folder outside the framework repository (AGENTS.md must not leak).

## Fairness notes

- The query contract (stage 2) is pinned before any run; if `EntityController<T>` answers it
  natively, the Koan arm's stage-2 cost is allowed to be zero lines of code — that is the claim
  under test, not a flaw in the comparison.
- Semantic probes (stage 3) are mechanically checked for token disjointness against the whole
  seed corpus before grading, so `LIKE`-style search cannot pass.
- Health ("check") accepts `/health`, `/health/ready`, or `/healthz` — arm-neutral.

## Traceability

Each stage's acceptance criteria derive from the framework's own validated receipts (table above):
the benchmark dogfoods the capabilities system — the same retrieval the `koan` skill hands the
agent is the source of the reference solutions the grader checks against.
