# Results — test01 staged composite · codex-oss-qwen38-27b · koan arm

- Harness: codex-cli 0.150.0 over Ollama via per-run OSS provider overrides
  (`wire_api="responses"`; Ollama implements `/v1/responses`; global codex config untouched and
  default-verified). Cell cap raised to 45 min for the local tier (both arms equal).
- Treatment: prompt v3 arm line (koan skill pointer) + SKILL.md v4

| Attempt | Environment | Wall clock | Outcome |
|---|---|---|---|
| 1 | repo with `local-feed/` fixture (0.8.x nupkgs, no README) | 634 s | agent discovered the fixture, read it as the intended source, ended turn mid-research — no project |
| 2 | fixture deleted | 665 s | agent read the skill, verified packages (found 1.0.x in global cache), ended turn during verification — **no file ever created** |

**Cell verdict: 0/1 after two attempts — task failure at the model-behavior level.**

## Findings

- The codex↔Ollama pipe itself **works**: real multi-minute turns, files read, commands executed,
  tool protocol intact. The opencode↔Ollama tool-calling gap does not apply here.
- The failing stage is **loop sustainment**: qwen38-27b (daily tuning) does competent research —
  reads the skill's greenfield block, checks sources and caches — then ends its turn before
  writing anything. Observed twice, under two environments, so the `local-feed` fixture is
  exonerated (it was attempt 1's confound, now removed from the repo along with the fixture
  itself).
- Practical consequence for the matrix: the local tier's blocking question has moved from
  "can the pipe carry tools" (yes, via codex) to "can a ≤27B local model sustain a build loop to
  completion." Next probes: the `qwen38-27b-q4-max` tuning, a different local family
  (gemma/mistral class), or an agentic-loop-tuned model.

Transcripts: attempt 1 in `koan-attempt1-fixture-trap/`, attempt 2 in `transcripts/`.
