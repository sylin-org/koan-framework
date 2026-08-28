# Results — test01 staged composite · agy-gemini · plain arm

- Harness: Antigravity agy 1.1.22 (Gemini-tier model, unpinned), unattended; one conversation
  resumed per stage; ran in the neutral folder via the scratch junction
- Treatment: plain arm line (no `Sylin.*`); stage bodies byte-identical to the koan arm's

| Stage | Battery | Wall clock | Note |
|---|---|---|---|
| 1 — CRUD + health + persistence | 9/9 | 93 s | full EF Core + SQLite app, built and self-verified |
| 2 — query every field | 16/16 | 34 s | |
| 3 — semantic search | 22/22 | 132 s | hand-rolled Ollama embeddings + ranking; all keyword-disjoint probes passed |

**Cell complete: 22/22 cumulative, ≈4.3 min.**

## Pair reading (agy-gemini, single run)

Both arms passed everything; the plain arm was ~8× faster end to end (4.3 min vs the koan arm's
30+ min, dominated by the koan arm's stage-1 cap hit during skill reading). On this tier the
skill's reading tax was the whole story: marginal stages cost nearly the same on both arms
(34/132 s plain vs 40/117 s koan). Contrast with codex-sol-high, where the same comparison also
favored plain. The matrix's remaining hope for a koan win is the success-rate axis at tiers
where models cannot drive tools at all (see opencode-qwen35-9b cells).

## Attempt history

Three earlier attempts failed on harness causes (flag parsing, cwd→scratch, a refusal after the
junction pre-created the neutral folder) — all fixed in the runner; no agent data from them.
Transcripts: `transcripts/events-s{1,2,3}.jsonl`.
