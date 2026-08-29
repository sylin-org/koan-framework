---
type: ARCHITECTURE
domain: framework
title: "Announcement Initiative Progress"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-08-27
framework_version: v1.0.12
validation:
  date_last_tested: 2026-08-27
  status: reviewed
  scope: initiative ledger initialization
---

# Announcement Initiative Progress

This is the initiative's only live status ledger. Update it in the same change that starts,
blocks, or completes a work item. The roadmap describes order; it does not report progress.

## Initiative state

- Overall: `active`
- Current tranche: `T0 — Receipts`
- Active work item: test03 contract-v3 pair re-fire (codex-sol-high, both arms)
- Active child: none

## Ledger

### 2026-08-28 — Contract v3 written: v1's first-class Ingredient structure + v2's earned shape pins; v2 attempts archived; grader smoke-proven both directions

- **Design.** v3 restores what v2 removed — Ingredient as a first-class entity with required
  endpoints (`GET/POST /api/ingredients`, id = lowercase name; recipe lines reference by name) —
  because v2's "ingredients do not need their own endpoints" line removed the structure that led
  the koan arm to model the join at all. Every v2 pin is kept byte-verbatim: match envelope
  `{title, missingCount, missing[]}` best-first, stats key, `{"count":N}` usage shape, conversion
  table (glass=240 ml, tbsp=15 ml, piece=1), presence-only lines, both semantic probes. The task
  text states HTTP observables only — endpoints and shapes, never a modeling approach — so it
  stays arm-neutral. Battery grows 12 → 14 checks (seed-ingredients, list-ingredients added).
- **Smoke receipts (standing rule: known-good AND known-bad before first use).** Graded the
  preserved v2 apps under the v3 grader. v2-plain app: 11/14 — every carried check reproduced its
  v2 pass, and the two new checks failed exactly (no ingredient endpoints exist); the extra
  `build` fail is the recorded inherited-repo-CPM artifact (NU1008) that only fires when a plain
  snapshot builds inside the repo tree — real plain-arm runs grade from the neutral folder, as in
  v2. v2-koan app: 7/14 — the hollow signature reproduced verbatim (usage-count got 0, stat got
  0, pantry "first=Big Feast missing=0") plus the two new fails. The grader discriminates in both
  directions before any agent time is spent.
- **v2 attempts archived in-cell**: `cells/test03-relationships-pantry/codex-sol-high/<arm>/
  attempt2-contract-v2/` (beside `attempt1-contract-v1/`), preserving both apps for exactly this
  kind of re-grading.
- **The re-run's question:** contract v3 + unchanged skill v6. If the koan arm now builds real
  relationship queries, the v2 hollow layer was contract-steering; if it hollows again, it is a
  skill gap and v7 (relationship compound in the one-block) is the measured counter. Either
  answer feeds the announcement chart. Pair firing is GPU-gated on operator confirmation
  (Ollama up, arms sequential on 5099).

### 2026-08-28 — Contract v2 pair: plain 12/12 perfect; koan 7/12 hollow relational layer — the campaign's sharpest negative

- **Plain arm: 12/12 in 597 s** — join modeling, usage-count (=3), cross-unit conversion filter,
  >10 stat, fully-ranked pantry match (Salted Pasta first missing-0, Big Feast last missing-8),
  restart persistence, both semantic probes. A complete, correct implementation of the whole
  contract.
- **Koan arm: 7/12 — the relational layer is hollow.** CRUD, semantic probes, restart pass; but
  usage-count = 0, conversion filter wrong, pantry match reports every recipe fully covered (the
  matcher does not compare). The agent modeled ingredient lines as unindexed embedded data rather
  than declaring `[Parent]` join entities — the relationship grammar it needed was one
  skill-descent away, and it never descended.
- **Root-cause chain (recorded, not speculated):** contract v2's "ingredients do not need their
  own endpoints; auto-register by name" is textually arm-neutral but not effect-neutral — it
  removed the structure that leads the koan arm to model Ingredient as a first-class entity, and
  without that entity there is no `[Parent]` edge and no relationship query to hollow out. The
  plain arm has no relationship surface to under-use, so it simply built the join correctly.
- **Standing consequence:** the skill's one-block (v6) does not mention `[Parent]` or the
  relationship query pattern, and the promoted leaf only landed during this campaign. Skill v7
  (relationship compound in the one-block) is the direct counter, to be measured as its own
  treatment version — WEB-0073's lesson one level up: capabilities an agent cannot find do not
  exist.
- Framework backlog candidate from the same evidence: a scaffold/analyzer-level guard or template
  that surfaces the relationship query pattern whenever lines reference other entities by name.
  Filed as a dogfood-loop output.

### 2026-08-28 — Chain day: claude pair completed ($6.21 plain, 22/22); test03 column run — grader/contract shape investigation opened

- **Claude pair is complete.** plain arm: 22/22 in ≈20.2 min for **$6.21** (vs koan arm 22/22,
  ≈30.9 min, $12.37). Both claude rows are the strongest A02 candidates: demonstrated, cheap to
  repeat, harness-reported cost.
- **test03 (relationships/pantry) ran on codex-sol-high, both arms — and stopped honestly.**
  Koan 9/12, plain 8/12. The relational queries the task probes **passed on both arms**:
  usage-count by name (=3), the cross-unit conversion filter (≥300 ml caught 480/300 and
  excluded 15 tbsp), the >10-ingredients stat, restart persistence, and semantic probe 1 on both
  arms (probe 2 missed on plain only). The shared failure signature — create-with-embedded-lines
  and the pantry-match response shape, on both arms — redirects suspicion to the task contract's
  create-shape clarity and the grader's match parsing. Investigation queued before re-grade;
  neither attempt counts as a model verdict.
- Chain also re-confirmed: claude's staged-composite stage-2 battery (16/16) matches the
  test03-adjacent q-and-* checks, and semantic probes held on the koan arm under skill v6.

### 2026-08-28 — test02 MCP-enforcement column: first quality-axis datapoint (koan 13/13 zero-leak; control 0 leaks, 2 artifacts)

- `matrix/tasks/mcp-enforcement/` executed on codex-sol-high, both arms. Adversarial battery:
  two MCP sessions (anonymous + member) over Streamable HTTP, advertisement check
  (no mutation tools offered to anonymous callers), enforcement probe (anonymous write attempt),
  field projection (`cost` absent from every anonymous surface, present for members), plus the
  HTTP battery.
- **Koan arm: 13/13, LEAKS 0, 653 s** — `[Access]` + `[McpEntity]` declarations produced a
  zero-leak governed MCP surface with zero security code written by the agent.
- **Plain arm: 11/13, LEAKS 0, 568 s** — the control built a real MCP server via the official C#
  SDK in stateless Streamable HTTP mode and gated it correctly on this run. Two check failures
  were grader artifacts, recorded as such: cell snapshots inherit repo CPM (NU1008 on rebuild),
  and the lenient tool-matcher called `update_recipe` without its required id. Both artifacts
  have named fixes in the cell receipt.
- Grader defect found and fixed during grading: a literal `mcp-session-id: none` fallback broke
  evaluation of stateless servers — session header is now conditional. This is the third
  harness-defect class the column execution has caught, all recorded.
- Honest read: on this single run the control held (trained on the official MCP SDK) — the
  quality headline awaits A02 repeats (does the control's gating hold across 5 independent
  assemblies?) and the other harness rows. n=1; nothing publishes yet.
- **Skill v6 A/B (verify-once pattern): inconclusive-to-negative at n=1.** v6 run measured 838 s,
  11/13 (member-MCP checks failed this run), build/run cycles unchanged at 12 — vs v5's 653 s,
  13/13. Zero leaks both. Recorded honestly: a sequencing sentence does not move frontier-model
  loop behavior; if the lever is pursued it must be mechanical (a canonical probe script shipped
  beside the skill). LEAKS 0 maintained across both skill versions.

### 2026-08-28 — WEB-0073 comprehensive verb-surface coverage landed (Web AdapterSurface TestKit)

- Six new specs in `AdapterSurfaceSpecsBase` (inherited by all eight Web AdapterSurface adapter
  suites): PUT create-by-route-id, PUT replace, PUT 409 mismatch corrective (+ anchored entity
  untouched), PUT body-id-agrees accepted, PATCH merge-patch null-clears, PATCH partial-json
  sets-only-sent.
- Proven green on InMemory (11/11) and Sqlite (11/11) adapter suites; PatchOps regression 14/14.
  The remaining six adapter suites (Postgres, SqlServer, Mongo, Redis, Couchbase, Json) run the
  same inherited specs in CI; Docker-gated ones green-skip without infrastructure.
- Live-host manual proof (PUT create/replace/409, S01 battery 7/7 on a bare controller) preceded
  this and caught the constructor-id interplay bug the committed tests would now also catch.

### 2026-08-28 — Dogfood loop closed: matrix finding → WEB-0073 governed PUT → verb map → skill v5

- The matrix's first framework requirement landed. Origin chain: codex-sol spent exploration
  turns discovering the update-verb shape; qwen38-27b stalled on it entirely — a cold-start gap,
  measured twice before it was fixed.
- **WEB-0073** (ADR + implementation): governed `PUT /{id}` on `EntityController<TKey>` — option 2
  accepted (request-carried `RouteId`, applied by `EntityEndpointService.Upsert` before the
  create-vs-update split; corrective `InvalidOperationException` when an entity exposes no
  writable id). PUT body id is checked/normalized at the JSON level — the proof host caught an
  interplay bug (the Entity constructor assigns an id at bind time, so the bound model never
  shows a default id; the bound-model approach 409'd every request).
- **Proven on a live host**: PUT create-by-route-id, replace, `409 web.put.idMismatch`, PATCH
  merge-patch regression, and the agent-race S01 battery **7/7 against a bare
  `EntityController<Recipe>;`** — no delegator. Build 0 errors; PatchOps suite 14/14.
- **Verb map shipped**: `docs/capabilities/web/entity-api.md` now states the surface (POST =
  upsert · PUT = replace · PATCH = delta by content type · DELETE = remove) — the update story
  was previously discoverable only by reading source.
- **Skill v5**: one-block carries the verb note and the sequencing line ("write the complete
  draft before verifying anything") — the direct counter to the research-without-commitment
  failure mode measured in the qwen38 cells.
- Standing rule engaged: skill v5 is a new treatment version; headline matrix cells re-baseline
  before any published number. Next matrix run is also the v4→v5 A/B.

### 2026-08-28 — Matrix launch: three harnesses on the staged composite; local tier produces the low-end datapoint

- Restructured `evals/agent-race/` into the cell layout (`matrix/cells/<test>/<model-harness>/<arm>/
  {results.md, transcripts/, code/}`, gitignored code+transcripts, committed receipts), added the
  grading lock (mkdir-based; first attempt crashed the grader under `set -u` on an unbound
  variable) and the gate hole fix (an empty grade file used to pass vacuously — found via a false
  ALL-STAGES-PASSED on the first opencode launch).
- **claude-default (Opus-class) koan cell: 22/22**, ≈30.6 min, $12.37 harness-reported cost.
  Claude Code needed `--verbose` with stream-json; its runner stalled once post-turn (worked
  around). Its plain pair is **blocked by the operator's monthly spend cap** — operator action
  required.
- **agy-gemini koan cell: 22/22** (stage 1 hit the 30-min cap mid-self-verification, state
  passed; stages 2–3: 40 s / 117 s). Harness defects fixed: agy ignores process cwd and works in
  `~/.gemini/antigravity-cli/scratch` (solved by junctioning scratch onto the cell folder —
  `--add-dir` grants access but does not relocate the default workspace), and `-p` must carry
  the prompt as its value. agy-plain in flight.
- **opencode + local qwen35-9b (≤12 GB) koan cell: 0/9 — the model never engaged its tools**
  (one turn, ~97 output tokens, no files). The strongest low-tier datapoint yet: at this tier the
  question is not speed but whether an application exists at all. Caveat recorded: an
  opencode↔Ollama tool-calling gap cannot be separated from model ceiling in this run. Provider
  config added to opencode (OpenAI-compatible → Ollama), permissions opened for unattended runs.
- Gemini CLI itself is retired by Google for individual accounts (`IneligibleTierError`);
  antigravity `chat` is GUI-bound — the agy CLI is the working Gemini-tier harness.
- Honest standings: the frontier plain control still leads every timed stage on codex; the low
  tier fails on task existence, not speed. The decisive open question is unchanged: does the
  koan skill flip *success rates* at low tiers — requiring a local model that can drive tools
  (a second local model, or a fixed tool-calling path).
- Next: agy-plain completion; claude-plain after spend-cap raise; second local model; then A02
  (≥5 per arm on headline cells).

### 2026-08-28 — Staged composite (flagship scenario) run paired: both pass; frontier control wins

- Built `evals/agent-race/staged-composite/`: one session, three sequential stages (CRUD+health →
  query-every-field → semantic search via local Ollama), accumulating grader batteries
  (9 → 16 → 22 checks), capability-traceable stage receipts, keyword-disjoint probe design.
  Dogfoods the capabilities system: each stage's acceptance derives from a validated recipe.
- **Koan arm: all stages passed** (9/9 → 16/16 → 22/22; 787/501/471 s). Marginal additions were
  additive; the agent self-verified provider election and corrective failure at every stage.
- **Control arm: all stages passed, faster at every stage** (226/246/330 s; ~0.85 M vs ~3–4.6 M
  input tokens). The control hand-rolled Ollama embeddings and ranking in 5.5 minutes and passed
  every keyword-disjoint probe.
- **Honest finding: the crossover hypothesis failed on a frontier model.** Charter claim 3
  (agent-amplified development) cannot publish on this evidence for `gpt-5.6-sol`-class models.
  The decisive follow-up is the same composite on lower model tiers — the hypothesis narrows to
  "the skill matters where model knowledge ends," which is testable before any launch copy is
  written. This is the receipt process working: the claim was caught before the announcement, not
  after.
- Harness hardening recorded: grader `set -u` crash fixed; session handoff uses explicit
  `thread_id` (ghost-session conflict found); codex invoked with cwd inside the working folder
  (a `-C`-only run leaked into the shared repo folder); per-arm artifact directories prevent
  transcript overwrites. Discarded attempts preserved under `attempts/`.
- Cost note: the koan skill's reading tax is now the dominant measurable overhead. SKILL.md
  brevity is an optimization target with a measurement loop attached.

### 2026-08-27 — Skill v4 (greenfield one-block) measured: cold-start cost halved

- Optimization applied to `.agents/skills/koan/SKILL.md`: new opening section "Greenfield: one
  block to a running app" — complete contiguous skeleton (csproj with exact package refs,
  Program.cs, Entity, EntityController, appsettings), an altitude-routing sentence ("descend only
  when the task names something the block does not cover"), and template-first for empty
  directories. Skeleton transcribed from the graded v3 project; task-agnostic; skills-lint
  passed.
- Rationale from telemetry: prior runs showed agents assembling skeletons from scattered reading
  (v1 read advanced samples for a CRUD task) and never using the `koan-web` template; input
  tokens were 95% cached, so the cost was reading turns, not volume.
- Measured (single run, 7/7): **279 s / 1,359,842 input tokens**, vs skill v3's 404 s / 2.44 M —
  wall clock −31%, input tokens −44%. Control remains faster on plain CRUD (203 s / 496 K).
  Proof culture intact: the v4 agent still verified provider election via facts, health,
  restart persistence, and corrective failure.
- Skill-version history recorded in the S01 run record (v2 321 s, v3 404 s, v4 279 s; control
  203 s). All numbers single-run until A02's ≥5 per arm.
- Next: climb the ladder — S02 (semantic search, Ollama `nomic-embed-text` present) is where the
  control must hand-roll what it does not know cold; that is the crossover test.

### 2026-08-27 — Treatment refined: the koan skill is the treatment; S01 re-run canonical

- Operator decision: the ONLY arm difference is that the Koan prompt points Codex at
  `.agents/skills/koan/SKILL.md` with "read it and follow it." The skill routes the agent into
  docs itself. The skill is deliberately NOT installed globally — global skills are visible to
  both arms and would contaminate the control.
- S01 Koan arm re-run under prompt v3 (skill-pointer): **404 s, 7/7**, 2,443,824 input
  (2,328,064 cached), 14,051 output. All three treatment-version runs (v1 340 s, v2 321 s, v3
  404 s) passed 7/7; v3 is canonical and archived predecessors live under
  `evals/agent-race/attempts/`. The skill-routed agent reads more: higher context and ~80 s over
  v2 — measured as part of the treatment, not adjusted away.
- Control arm unchanged (203 s, 7/7); its prompt was not touched by this refinement, so its
  canonical run stands.
- Updated S01 pairing: control 203 s vs Koan 404 s on plain CRUD — the grammar-learning cost is
  real and belongs in any honest launch narrative. The crossover hypothesis (S02–S06) is now the
  whole ballgame.

### 2026-08-27 — A01 harness built; S01 both arms executed (single canonical runs)

- `evals/agent-race/` created: series README + fairness rules, S01–S06 capability ladder
  (cumulative recipe domain, one pillar per rung), HTTP-only S01 grader, per-arm runners.
- Agent under test: local `codex exec` (codex-cli 0.150.0, model `gpt-5.6-sol` @ high, the
  workspace default), 30-minute hard cap enforced in-prompt and via `timeout 1800`.
- **S01 results (single canonical runs, not medians):**
  - Koan arm: 321 s, 7/7 checks, 1,653,877 input tokens (1,557,504 cached), 10,959 output. The
    agent learned the grammar from the repository and self-verified provider election, health,
    and restart persistence unprompted.
  - Control arm (neutral folder, AGENTS.md excluded): 203 s, 7/7, 495,697 input (446,208 cached),
    7,299 output. Plain CRUD is the control's training-data home turf.
- Honest S01 reading: control is faster and cheaper on the simplest rung. The crossover
  hypothesis (Koan wins where the control must hand-roll semantic plumbing, durable jobs, MCP
  surfaces) is what S02–S06 test. No magnitude claim exists until A02 closes with ≥5 runs per arm.
- Threat to validity found and fixed: the globally installed `explore` skill's plan-approval gate
  stalled the first control attempt (33 s, no code); both prompts now carry an identical
  unattended-run sentence. Preliminary attempts preserved under `evals/agent-race/attempts/`.
- Runner defects found and fixed: wrong relative repo path for the in-runner grader; control-arm
  artifact copy-back missed a root-level project (source for that run not preserved; transcripts
  and verdict are).
- Next: A01 smoke acceptance is met (one full run per arm recorded). Continue S01 to ≥5 runs per
  arm, then materialize S02 prompts (Ollama `nomic-embed-text` verified present) and climb.

### 2026-08-27 — Initiative opened; baseline captured

- Initiative created: charter, roadmap, acceptance gate, ledger, handoff, work items A01–A10.
- Pre-announcement baseline recorded into [CHARTER.md](CHARTER.md) from live sources:
  4 stars / 3 forks / 0 watchers; 14-day traffic 18 views (7 unique) vs 887 clones (196 unique,
  ~90 CI runs in window); referrers github.com only; NuGet family 181,402 downloads across 201
  packages with the `Sylin.Koan` bundle at 2,042 and `Sylin.Koan.Templates` at 1,191; zero
  social/community footprint anywhere.
- Positioning research recorded in the charter's environment section: ASP.NET Boilerplate
  end-of-support (May 2026), MCP standardization with governance as the live concern, .NET 10 LTS,
  and the "Koan" search-name collision hazard.
- Next: [A01 — agent-race benchmark harness](work-items/A01-agent-race-benchmark.md).
