# Results — test01 staged composite · agy-gemini · koan arm

- Harness: Antigravity agy 1.1.22 (Gemini-tier model, unpinned), unattended; one conversation
  resumed per stage
- Treatment: prompt v3 arm line (koan skill pointer) + SKILL.md v4

| Stage | Battery | Wall clock | Note |
|---|---|---|---|
| 1 — CRUD + health + persistence | 9/9 | 1800 s (cap hit) | agent built the full app, then hung in its own verification script; the 30-min cap killed it and the state passed the full battery |
| 2 — query every field | 16/16 | 40 s | see interpretation caveat |
| 3 — semantic search | 22/22 | 117 s | all three keyword-disjoint probes passed |

**Cell complete: 22/22 cumulative.**

## Interpretation caveats (recorded, not hidden)

- Stage 1's wall clock is the cap, not organic time: the agent was killed mid-self-verification
  with the application already built. A longer cap would likely show a faster organic pass.
- Stage 2's 40 s is consistent with the hypothesis that `EntityController<T>` answers field
  queries natively — the agent's resume turn may have needed zero code. Stage 3's 117 s is
  plausible for an additive `[Embedding]` + connector change on a warm build, but both marginal
  numbers need transcript analysis before any claim uses them (A02).
- Harness defects found and fixed during this cell: agy ignores process cwd and works in its
  global scratch (`--add-dir` grants access but does not relocate the default workspace; solved
  by junctioning the scratch path onto the cell's neutral folder), and `-p` must carry the prompt
  as its value (an unattached `-p` swallows the next flag).
- Transcripts: `transcripts/events-s{1,2,3}.jsonl`.
