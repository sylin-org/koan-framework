# Results — test01 staged composite · codex-oss-qwen38-27b-max · koan arm (skill v5)

- Harness: codex-cli 0.150.0 over Ollama (`qwen38-27b-q4-max`, 100% GPU); cap 45 min/stage
- Treatment: **skill v5** (verb surface in the one-block + draft-before-verify sequencing)
- Outcome: **0/1 — cap hit at stage 1; code/ empty**

## v4 → v5 A/B verdict (koan arm)

**v5 did not flip this cell.** Under v5 the agent ran the full budget again — but this time it
got *further before stalling*: the `local-feed` trap is gone (deleted), the packages resolved
(transcript shows it enumerating the correct `Sylin.Koan.* 1.0.x` versions from the public feed —
the verb surface it stalled on in v4 is now native and documented in the one-block it read). The
remaining blocker is budget arithmetic: a 27B local model at GPU speed turns slowly, and the
composite's stage 1 (multi-file scaffold + build + verify) exceeds 45 min before the loop
commitment point. The plain arm failed the same cap while *actively writing* — its apply-patches
repeatedly died on a PowerShell parser error (diff-formatted lines pasted into the shell), a
codex-on-Windows tooling friction independent of the task.

## Recorded for the next local-tier attempt

1. **Tier cap is the binding constraint, not skill content**: success-rate questions for this tier
   need a longer budget (90–120 min) recorded as a tier-specific harness parameter — never mixed
   into speed comparisons.
2. **The apply-loop friction is real**: the plain arm's visible failure was diff-lines-in-shell
   parsing. An auto-continue/apply-retry harness loop attacks this directly.
3. v5's sequencing line did not induce earlier file writes at this scale — the model never
   reached the write phase to benefit from it.

Attempt 1 (skill v4) archived at `attempt1-skillv4/`.
