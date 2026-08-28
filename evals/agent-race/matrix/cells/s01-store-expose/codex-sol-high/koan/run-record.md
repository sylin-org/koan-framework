# Run record — S01, pure-agentic (Koan arm), canonical attempt (skill v4: greenfield one-block)

- Date: 2026-08-27
- Agent: codex-cli 0.150.0, model `gpt-5.6-sol` @ `high`, unattended `codex exec`; prompt
  unchanged from v3 (names Koan, points at `.agents/skills/koan/SKILL.md`)
- Treatment change under test: SKILL.md v4 — new opening section "Greenfield: one block to a
  running app" (complete contiguous skeleton: csproj with exact package refs, Program.cs, Entity,
  EntityController, appsettings; altitude-routing sentence; template-first note). skills-lint
  passed; skeleton transcribed from the v3 graded project, task-agnostic.
- Project: `project/` — lean shape (Recipe, RecipesController, Program, constants), SQLite
- Wall clock: **279 s** (v3 was 404 s; control 203 s)
- Tokens (session totals): input 1,359,842 (cached 1,280,768), output 9,868 (v3: 2,443,824 /
  14,051)
- Grader: `graders/grade-s01.sh` — **7/7** (build, start, create, list, update, delete,
  persistence-across-restart)

## Findings

- The one-block skeleton cut the cold-start cost roughly in half on both axes: wall clock −31%
  (404→279 s), input tokens −44% (2.44 M→1.36 M) versus skill v3, single runs.
- The proof culture survived the optimization: the agent still verified SQLite election via
  runtime facts, health 200, restart persistence, and corrective failure on invalid provider
  configuration, and reported a zero-warning build.
- Gap to control on S01 narrowed from 200 s to 76 s and from 4.9× to 2.7× input tokens. Remaining
  gap is consistent with the skill-read turn plus the agent's verification work, which is treated
  as product, not overhead. Single-run numbers; A02 medians (≥5 per arm) required before any
  claim publishes.

## Skill-version history for S01 (all 7/7)

| Skill version | Treatment | Wall clock | Input tokens |
|---|---|---|---|
| v2 | repo-root pointer (prompt v2) | 321 s | 1,653,877 |
| v3 | skill pointer, pre-one-block | 404 s | 2,443,824 |
| **v4 (canonical)** | **skill pointer + greenfield one-block** | **279 s** | **1,359,842** |
| control | no Koan treatment | 203 s | 495,697 |
