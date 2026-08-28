# Results — test01 staged composite · opencode-qwen35-9b-local · koan arm (attempt 1)

- Harness: opencode 1.18.21, model `local-code:qwen35-9b-q4` via Ollama (local, 6.6 GB — under
  the 12GB tier)
- Treatment: prompt v3 arm line (koan skill pointer) + SKILL.md v4; identical stage bodies
- Wall clock stage 1: 71 s; grader produced no score line before failing the build check

| Stage | Battery | Wall clock | Outcome |
|---|---|---|---|
| 1 | 0/9 (build/start/health failed; no app produced) | 71 s | **task failure — agent never engaged its tools** |

## Finding (the low-tier datapoint, honestly recorded)

The 9B model, given the Koan skill and the identical prompt, ran **one turn and stopped**: ~97
output tokens, zero file writes, zero tool executions. The transcript shows a conversational
response, not agentic work. Whether this is the model's ceiling or an opencode↔Ollama
tool-calling integration gap cannot be separated in this run — both are part of the measured
"local-tier ecosystem cell."

This is exactly the failure mode the matrix's low tier exists to surface: **the frontier pairs
argue about speed; the local tier argues about whether an application exists at all.** The plain
arm of this cell is still worth running for the pair, and a second local model (gemma/mistral
class) will separate "qwen-specific" from "tier-wide."

## Harness notes

- opencode provider configured via `~/.config/opencode/opencode.json`
  (OpenAI-compatible → `http://localhost:11434/v1`); permissions set to allow edit/bash for
  unattended runs.
- The first lane-D launch produced a **false ALL-STAGES-PASSED** through a gate hole (an empty
  grade file made the score comparison vacuously true). The gate now fails on a missing score
  line in all four runners; this attempt is the first graded under the fixed gate.
