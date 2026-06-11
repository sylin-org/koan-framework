# 05 — Leverage Plan: What to Do, In What Order, As One Person

## §1 The minimal truth set

The smallest set of things that must become true for the stack story to hold. **It contains
zero new ambition — all five are finishing work on existing claims.** If these five cannot be
completed, the honest fallback positioning is "three projects that share an author and a
discovery substrate" — which is also a perfectly good story.

1. **Zen Garden builds from a clean clone** against published koi crates
   (`[patch.crates-io]` for sibling dev) — one line per dep once Koi publishes the closure
   ([04 R3](04-architecture-alignment.md)).
2. **One scripted, repeatable end-to-end demo on two real machines**: pond ceremony → moss
   serves actual mTLS (client auth on) → the mongodb orchestrator emits a replica-set
   connection string → a Koan app consumes it over a Koi-trusted channel. Today **every
   single link has a verified break** (per-boot 401 token; no client auth; zero Koan cert
   affordance; proxy excluded per R6). This demo is the Epic's existence proof and the ONLY
   new cross-repo artifact worth attempting in 2026.
3. **Each repo's front door true for its standalone story** before any stack page exists —
   "true" means a stranger on a clean machine completes the README in one sitting.
4. **Cross-repo contract discipline**: pinned released versions + the URI-corpus pattern
   extended to the actual integration protocols, run in each repo's CI
   ([04 R8c](04-architecture-alignment.md)).
5. **The survivability floor**: tags + minimal CI in all three repos, so the observed
   2-of-3-dormant steady state ([01 §4](01-stack-anatomy.md)) cannot silently break
   integration. Dormancy can't be ended by one person; it can be made safe.

## §2 Sequencing (serialized, like the maintainer)

**Wave 0 — The decision wave (days; no code).** One cross-repo ADR ratifying, in a single
sitting, the five conflicts from [03 §0](03-strategic-opportunities.md): the layering law +
names-never-flow-down (R1), MCP layering doctrine, discovery doctrine (R7), the joint AI
succession (ollama-now; contract on it; harvest ai-crate designs; trim Koan Training/Eval to
match), the honest sovereign composition (Mongo+Ollama v1), coupling form ("works alone,
lights up together"), and the trust topology (R10's two-fabrics-one-binding). This is the
Epic's uniquely cheap move — one architect, no committee — and it unblocks every repo's
stashes from contradicting each other.

**Wave 1 — Foundations, one repo at a time (each repo's own Stage-0/critical-path, already
prompted).**
- *Koi*: republish the crate closure incl. koi-udp (unblocks ZG); token-provisioning story,
  then bind flag (R5.4); truth restoration per its P01–P04.
- *Zen Garden*: koi deps → published versions; merge the June branch; first CI on the
  2,483-test suite; first tagged release; close :7185/`/deploy`; per its `.agentic/prompts`.
- *Koan*: sln coverage (39/87), CI re-enable, front-door truth per its 06-stash Track A/B —
  already sequenced there.
- Throughout: stand up the ≤3 reusable release-truth workflows (R8) as each repo gets CI.

**Wave 2 — The integration milestone.** Build truth-set item 2 (the two-machine demo),
fixing only what the demo's path requires: certmesh token provisioning, moss client-auth
(Phase 4 pulled forward only for this path), a minimal Koan truststore/CA-pinning affordance
(its first lines of certificate code, satellite package per R4). Film it; script it; make it
a CI-runnable artifact where feasible.

**Wave 3 — Freeze what the demo proved.** Generate OpenAPI for `/api/cluster/connect` and
the AI single endpoint; document `/v1/mdns/*` and `/v1/certmesh/*` as stable; publish
conformance fixtures (URI-corpus pattern); wire R8c cross-repo contract jobs; emit the R9
self-description envelope from all three. Contracts are frozen from demonstrated behavior,
not designed speculatively.

**Wave 4 — The opportunity ladder**, in [03](03-strategic-opportunities.md)'s order: §1
Agent-Ready LAN (koi-mcp P11 + Koan governed access — sequential, never parallel), §2 trust
fabric completion (CSR ADR, KSVID binding), §4 data-plane wedge demo, §5 governed ops, §6
composition audit. §3 (Win10) runs on its own clock — next section.

## §3 The Koan-side inversion (the host repo's stake)

Because this analysis is hosted in the Koan repo until moved: Koan's specific Epic
obligations, all satellite-shaped ([04 R4](04-architecture-alignment.md)):

1. Neutral `IOfferingResolver` + candidate-source extension point in Core.Orchestration
   (small ADR; generalizes an existing pattern with four consumers).
2. `Sylin.Koan.ZenGarden.*` satellites absorb the bindings, intent parsing, `KoiHandler`;
   the 119KB client shrinks to a generated client when ZG's OpenAPI exists.
3. **S3 split** (generic connector vs Moss presign satellite) — the severest mainline defect.
4. Training/Eval facades to the satellite or cut (aligns with Koan's own MLOps shed + the
   joint succession ADR).
5. Delete `Koan.ServiceMesh` + the raw multicast probe after the R7 latency check.
6. First certificate affordance (CA pinning / truststore check) in the satellite, for Wave 2.
7. The architecture test: no mainline csproj references `Koan.ZenGarden*` — the gate that
   keeps it fixed.

These slot into the existing 06/07 stash structure as one new track; they do not displace
the 07 build cards (P1 self-description in particular is *strengthened* — the lockfile
becomes the Koan payload of the R9 envelope).

## §4 The Windows-10 go/no-go rule (the only dated decision)

The ESU date (2026-10-13, ~4 months out) is Zen Garden's hook alone, and the stack framing
must never be allowed to capture it. Decision rule:

- **Mid-July checkpoint**: if ZG's cadence has recovered (Wave-1 ZG items merged: clean
  clone, CI, first tag) → cut a **Zen-standalone** launch for Oct 13, explicitly excluding
  Koi-fixing and Koan from the critical path (Koi ships embedded as-is; a Koan sample
  offering is demo content only if Wave 2 happens to be done — never a dependency).
- **If not recovered** → retarget to "old laptops + phones, evergreen" and drop the date.
  CasaOS's 34k dormant stars prove the audience exists year-round; missing a promised date
  is worse than not promising it.
- Either way: the launch carries the disclosed AI-methodology posture (ZG's 46%
  co-authorship disclosure as a documented, reviewable practice) — the Booklore failure mode
  is screened for by exactly this audience.

## §5 What this plan deliberately does not do

(Argued in [03 §8](03-strategic-opportunities.md); restated as operating constraints.)
No Epic marketing before truth-set green. No mono-repo or shared release train. No
cross-language code sharing — protocols and fixtures only. No parallel MCP pushes. No second
PKI built while certmesh-vs-KSVID is unadjudicated (Wave 0 settles it). No new hard sibling
reference anywhere, ever — "works alone, lights up together" is the only permitted coupling
form, enforced by R1's gates.

## §6 Operating rules for one maintainer plus agents

1. **One repo in flight at a time; finish the wave before rotating.** The cadence data shows
   rotation is how this estate actually works — plan *with* it: make every pause safe (tags,
   CI, pinned deps) rather than pretending parallelism.
2. **Decisions batch, implementation serializes.** Wave 0 exists because decisions are the
   only thing cheap to do across all three repos at once.
3. **Agents inherit the per-repo stashes; the Epic adds only seam cards.** Koan's 06/07,
   Koi's P01–P13, ZG's 16 prompts are already lesser-model-ready. The Epic's seam cards now
   exist as the **[prompt stack](prompts/README.md)** (CHARTER + E01–E16): the Wave-0
   cross-repo ADR (E01), the surface ledger (E02), the seam fixes + contracts (E03–E07),
   the trust column (E08–E10), the demo (E11), the envelope (E12), and the
   agent-ready-LAN/mission surfaces (E13–E16). Per-repo work stays in the per-repo stashes;
   do not duplicate it into an Epic backlog.
4. **Verification budget goes where the process under-spends it** ([02 §3](02-synergy-audit.md)):
   data planes, release engineering, clean machines, seams. Every agent session that touches
   a seam must end by running the seam's contract corpus against released artifacts — not
   sibling checkouts.
5. **The finishing discipline.** All three assessments independently conclude "no new
   invention required — it requires finishing," and the strongest predictor of failure in
   this portfolio is the next interesting design outrunning the unglamorous finish line. The
   minimal truth set is five items of pure finishing. Hold the line there before any
   second-act capability — on any layer.
6. **Capacitation is the scoreboard.** The mission (README) defines success as people
   enabled, not positions won: the first outside contributor is a strategic milestone, not
   an interruption (ZG's own assessment says exactly this); 2–3 recurring contributors beat
   any star count; docs are curriculum for people acquiring capability, not marketing;
   the disclosed-AI-methodology posture ships as a trust feature, not an admission. And the
   enabler doctrine ([03 §0.0](03-strategic-opportunities.md)) binds the plan itself: when a
   wave forces a choice between deepening the stack's own surface and shipping a feeder
   integration that capacitates more people sooner, the feeder wins.
