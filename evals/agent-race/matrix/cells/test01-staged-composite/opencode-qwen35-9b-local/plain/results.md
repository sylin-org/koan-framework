# Results — test01 staged composite · opencode-qwen35-9b-local · plain arm (attempt 1)

- Harness: opencode 1.18.21, model `local-code:qwen35-9b-q4` via Ollama (local, 6.6 GB)
- Treatment: plain arm line (no `Sylin.*`); identical stage bodies
- Wall clock stage 1: 218 s

| Stage | Battery | Wall clock | Outcome |
|---|---|---|---|
| 1 | 0/9 (partial `RecipeAPI/` scaffold; no build, no running app) | 218 s | **task failure** |

## Pair reading — the low-tier cell (local qwen35-9b, ≤12 GB tier)

Both arms of this pair failed identically at the task-existence level: koan arm 0/9 in 71 s (one
turn, no tool engagement), plain arm 0/9 in 218 s (scaffolded files, no build, no running app).
At this tier **neither framework nor plain stack produced a working application** — the deciding
factor is whether the model can drive the agent harness's tools at all, which sits below any
framework comparison. This is the matrix's first complete pair, and it defines the floor of the
crossover chart: the local 9B tier is, today, not a viable agent target for either path on this
harness pair. Caveat (applies to both arms): the opencode↔Ollama OpenAI-compatible tool-calling
path may under-deliver tool fidelity; a different local model or harness would separate model
ceiling from integration quality.

Graded under the fixed gate (first cell in the matrix to run with the vacuous-pass hole closed).
