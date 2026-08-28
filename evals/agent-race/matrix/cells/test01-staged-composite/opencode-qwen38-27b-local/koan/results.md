# Results — test01 staged composite · opencode-qwen38-27b-local · koan arm (attempt 1)

- Harness: opencode 1.18.21, model `local-code-candidate:qwen38-27b-q4-daily` via Ollama
  (17 GB on disk — above the 12 GB VRAM line, viable as a "local upper tier" datapoint)
- Treatment: prompt v3 arm line (koan skill pointer) + SKILL.md v4

| Stage | Battery | Wall clock | Outcome |
|---|---|---|---|
| 1 | 0/1 (no csproj produced) | 634 s | tool calls emitted, none executed |

## Finding — root cause of the local-tier failures, now confirmed

The capability smoke **passed**: given "create hello.txt", qwen38-27b drove opencode's tools and
wrote the file (via the same opencode↔Ollama path). Given the real composite prompt, the model
**emitted tool calls that opencode never executed** — the transcript ends on a
`step-finish (reason: "tool-calls")` with an empty working directory. Small prompt: tools work.
Large prompt: tool calls drop.

This confirms the caveat carried by the qwen35-9b cells (both arms, 0/9): the local-tier failure
is dominated by the **opencode↔Ollama OpenAI-compatible tool-calling path**, which under-delivers
tool fidelity on real tasks — not solely by model ceiling. The 9B and 27B results are consistent
under that explanation.

## Verdict for the matrix

The opencode+Ollama lane is not yet a valid instrument for the low tier at task scale. The next
lever is a different local harness: **codex with an OSS provider config pointed at Ollama**
(codex implements its own tool protocol rather than relying on OpenAI function-calling fidelity),
same model, same prompts. Until then the ≤12 GB / local-upper tier rows stay "harness-blocked,"
recorded as such rather than as model results.
