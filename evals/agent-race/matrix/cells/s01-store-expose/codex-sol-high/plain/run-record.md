# Run record — S01, plain-agentic (control), canonical attempt-01

- Date: 2026-08-27
- Agent: codex-cli 0.150.0, model `gpt-5.6-sol` @ `high`, unattended `codex exec`; global skills
  present (symmetric with the Koan arm); ran in a neutral folder outside the framework repository
- Prompt: `prompt-plain.txt` v2 (byte-identical to the Koan arm's except the first line)
- Project: single-project minimal API with EF Core + SQLite (`recipes.db`), no Sylin packages
- Wall clock: **203 s** (cap 1800 s)
- Tokens (session totals): input 495,697 (cached 446,208), output 7,299
- Grader: `graders/grade-s01.sh` (HTTP-only; identical for both arms), invoked by the runner
- Result: **7/7** — build, start, create, list, update, delete, persistence-across-restart

## Findings

- On the simplest rung the control is faster and cheaper: 203 s vs the Koan arm's 321 s, and ~30%
  of the input-token cost. The model writes ASP.NET Core + EF Core from training knowledge without
  reading anything, while the Koan arm spends context learning the grammar from the repository.
  This is the expected shape of the crossover hypothesis: the Koan advantage should appear on
  rungs where the control must hand-roll what it does not know cold (S02 semantic plumbing, S03
  durable jobs, S05 MCP surfaces, S06 composition), not on plain CRUD.
- The pre-v2 control attempt stalled entirely on the global `explore` skill's plan-approval gate
  (see `attempts/pre-v2-stalled-plain/`); the v2 unattended sentence fixed it. Control-arm
  fragility around approval gates is a recorded threat to validity.

## Record-keeping defect (fixed in runner)

The control built its project directly in the neutral root rather than a `project/` subfolder, so
the runner's copy-back step captured transcripts and grade output but deleted the project source
during neutral-folder cleanup. Grading had already completed (7/7). `run-plain.sh` now snapshots
the whole neutral folder before cleanup. Source code for THIS run is therefore not preserved —
recorded here honestly; transcripts and verdict are.
