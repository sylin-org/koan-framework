# Capability validation protocol

How a capability moves from *shipped* to *proven* — reproducibly, by anyone, including an agent
with no conversational context. This is the companion to [`recipe-runs/PROTOCOL.md`](../recipe-runs/PROTOCOL.md):
that file governs executing a single recipe as a cold evaluator; this one governs the **full
validation cycle** for a capability (or pillar slice), from dev-source probe through published-feed
proof to an independently timed consumer test.

## The three-stage loop

### Stage 1 — Dev-source probe (fast iteration)

Build the smallest honest application for the capability against **project references** into
`src/` — never packages. Iterate here because cycles are seconds, not publishes.

- Scaffold via the standard template (`dotnet new koan-web` unless the capability says otherwise);
  add only what the capability node lists.
- Follow the node + its linked recipe **as written**. Every gap you paper over is a defect:
  record it instead of silently working around (see Harvest below).
- Prove the three legs: **behavior** (the node's promised journey), **composition** (the intended
  provider actually won — facts/lockfile/banner), **correction** (failure surfaces with a useful
  reason, never silent fallback or empty success).

Exit when the journey is green on source. Capture actuals verbatim — ids, status codes, ranked
results, election lines. "It worked" is not evidence.

### Stage 2 — Feed proof

Repeat the same journey against **published packages only** (`Sylin.*` from nuget.org). This is
where repo-ahead-of-feed skew becomes visible: code right, docs still wrong is still a documentation
defect. Record resolved package versions; the node's verified floor becomes
"X or newer (patch releases compatible)" based on what this stage actually used.

If Stage 1 passes but Stage 2 fails on missing behavior: the fix ships before the validation lands.
Do not lower the floor claim to make the gate green.

### Stage 3 — Template + independent consumer, timed

Distill the golden path into a NuGet-based template (or the closest equivalent artifact), then hand
a **subagent** exactly two things: the template invocation and the capability node. No conversation,
no session memory, no access to your findings. Measure wall-clock from scaffold to green journey.

**Target: under five minutes to first success.** Over budget means the next feedback cycle fixes
whatever ate the time — usually a missing using, an unstated config key, an unexplained wait, or a
silent provisioning step. Repeat until the subagent passes within budget twice consecutively.

The subagent's transcript is itself harvest: read its final state and dead ends as forensic
evidence of what the docs failed to say, even on a passing run.

## Cold-evaluator rules

All stop rules, mindsets, and blocker-report formats from
[`recipe-runs/PROTOCOL.md`](../recipe-runs/PROTOCOL.md) apply at every stage: stop at the first
BLOCKER/CONFUSING obstacle; do not debug framework internals to unblock yourself during evaluation;
the blocker report is the deliverable. Switching to maintainer role happens **after** capture — fix
forward, then re-run from the top of the current stage.

## Harvest obligations

A validation run is not finished when the journey is green. It is finished when:

1. **Node marked** — frontmatter carries `date_last_tested`, `status: passed`, and a scope line
   naming the path exercised (e.g., "Ollama path, save→search→rank over HTTP").
2. **Verified floor stated** — exact package versions from Stage 2, phrased "X or newer".
3. **Friction filed** — every gap found becomes one of: doc fix (same commit), corrective-error
   improvement (code commit + register entry), or a triaged item in the capability directory's
   shaping plan. Nothing observed is allowed to evaporate.
4. **Symbol origins closed** — if a snippet needs a `using`, namespace, config key, or environment
   fact to compile/run, the snippet's document now states it inline. No exceptions.
5. **Timings reported** — self-reported phase timings (scaffold/build/boot/journey); they are the
   docs performance profile and the <5-minute ledger.

## Where results land

- Node/recipe frontmatter (validation blocks) — committed together with any doc edits.
- Friction backlog — capability directory shaping plan; cross-link external consumers' registers
  rather than duplicating them.
- Templates — `Sylin.Koan.Templates` additions follow the release playbook; until published, Stage 3
  may pack templates locally but must say so in the evidence.
- Session notes — `local/NOTES.md` (untracked); durable lessons — the maintainers' learning pages,
  one pointer each, no duplication.
