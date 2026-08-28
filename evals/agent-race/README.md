# Agent race — pure-agentic series (A01/A02 execution)

Measure how fast an unattended coding agent reaches a fixed outcome with Koan versus plain
ASP.NET Core, across a ladder of scenarios that grows one pillar per rung.

## Arms

| Arm | cwd | Instruction difference |
|---|---|---|
| `pure-agentic` (Koan) | `evals/agent-race/pure-agentic/scenarioNN/project` (inside this repo) | First line: "Build this as a Koan application" plus the path to `.agents/skills/koan/SKILL.md` with "read it and follow it." The skill is the treatment; the skill itself routes the agent into the framework's docs. |
| `plain-agentic` (control) | neutral folder **outside this repo** (AGENTS.md must not leak) | First line: "Build this as a plain ASP.NET Core web API. Do not use Koan or any Sylin.* packages." |

Everything else in the two prompts is byte-identical. The Koan skill is deliberately not installed
into the global skills directory: global skills are visible to both arms (the `explore` skill
proves they fire unprompted), which would contaminate the control.

## Fairness rules (binding, from the initiative charter)

- Identical task text apart from the one arm-defining sentence; prompts are committed verbatim.
- Same agent (Codex CLI 0.150.0), same model (workspace default, "Sol high"), fresh session per
  run, no memory across runs or arms.
- 30-minute hard cap per run, enforced twice: in-prompt (agent must abort and write `ABORTED.md`
  with rationale) and externally (`timeout 1800`).
- Wall clock recorded from process start to process exit; token counts from the codex event log.
- Control arm may use any OSS package; only `Sylin.*` is excluded from it.
- Graders speak HTTP + JSON only; they never read the agent's code.
- At least five runs per arm per scenario before any median is published (A02).

## Execution mechanics

```bash
# Koan arm
evals/agent-race/pure-agentic/scenario01/run-koan.sh

# Control arm (copies artifacts back from the neutral runtime folder)
evals/agent-race/plain-agentic/scenario01/run-plain.sh
```

Each runner: stamps start/end wall clock, runs `codex exec` unattended against the arm's prompt,
then invokes the scenario grader, and writes `run-record.md` beside the artifacts.

Codex runs with `--dangerously-bypass-approvals-and-sandbox` because unattended Windows runs need
NuGet restore, port binding, and app restarts; the run is bounded by the isolated project folder
and the external timeout. This is recorded here as a known deviation from a sandboxed ideal.

## Scenarios

See [LADDER.md](LADDER.md). Each scenario's grader lives under `graders/` and is written before
the scenario first runs.
