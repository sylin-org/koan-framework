---
type: ARCHITECTURE
domain: core
title: "Agent-Extensible Adapters (strategic brief)"
audience: [architects, ai-agents]
status: draft
last_updated: 2026-06-21
---

# Agent-Extensible Adapters (strategic brief)

> The structural answer to the **"owns-every-axis = adoption lock-in"** barrier surfaced unanimously by
> the tenancy external review: make the framework able to **direct an agent to author a conformant
> adapter** for un-owned infrastructure, with an **objective conformance kit** as the acceptance gate.
> Framework-wide (not tenancy-specific); the tenancy *external-infra delegation seam* is the pilot and
> highest-stakes instance. Status: draft — emerged from the post-round-3 architect dialogue. A
> distributable feedback request lives in
> [agent-extensible-adapters-rfc.md](./agent-extensible-adapters-rfc.md).

## 1. The thesis

Most frameworks that call themselves "agent-native" mean *agents can use the framework.* Koan's claim is
stronger: **the framework extends itself through agents.** Agent-native has two halves:

- **Consumption** — agents *read/use* the framework (the projection / MCP work; see
  [agent-native-projection strategy] and the MCP explorer console).
- **Contribution** — agents *extend* the framework: author adapters for un-owned infra, **gated by
  conformance**.

The set of supported adapters is no longer bounded by what the maintainer wrote — it is bounded by what
an agent can **generate and verify** on demand.

## 2. The barrier it defuses

The tenancy review's unanimous fatal adoption barrier: a team wants Koan's tenancy/isolation but is
mandated to run Pinecone vectors or an enterprise Kafka bus Koan does not own. Today that is "rip out
your stack or walk away." With agent-extensible adapters:

```
koan adapter new --seam vector --provider pinecone
  → an agent reads the vector-seam Agentic Blueprint
  → writes the adapter
  → runs the conformance kit against a real Pinecone instance
  → GREEN = shippable
```

The lock-in evaporates, and the thesis softens from "**owns** every axis" to "**coordinates** every
axis, and can grow a new one on demand." It also loosens the otherwise greenfield-only go-to-market.

### The developer's view — a worked example

A developer, to their coding agent:

> *"I want to connect to our solution's database — it's an old Oracle server, I think. Check Koan's
> blueprints for anything we can use."*

The agent:

1. **Finds the blueprint** — matches the fuzzy intent ("old Oracle," a legacy SQL database) to the right
   Agentic Blueprint (*"How to connect to a legacy SQL database"*) from the blueprint catalogue.
2. **Checks for an existing adapter first (reuse before build)** — the blueprint's *first* instruction:
   *"search NuGet (and the Koan adapter catalogue) for an existing adapter for this target."* If one
   exists — first-party, or a community-published, conformance-verified one — **reference it and you're
   done** (Reference = Intent). Building is the *last* resort, not the first.
3. **Follows the research protocol, including the human-in-the-loop step the blueprint scripts** — *"ask
   the user for a **limited-permissions** connection string, so we can safely test access and probe the
   database."* (The connection string is itself `[Secret]`-class data — handled, never logged.)
4. **Empirically discovers capabilities** — connects with the least-privilege credential and **probes the
   live instance**: does it accept this query shape? does it support transactions? which operators push
   down? It announces *only* the Koan capability tokens it **confirmed by probing** — honest by
   construction.
5. **Builds a generic, conformant adapter** — not a hand-tuned Oracle adapter; a *correct* one that
   satisfies the obligations.
6. **Proves it** — runs the surface-validation conformance kit against that same real instance.
   Green = shippable.

The developer said one sentence and barely touched the keyboard; the agent did the safe, empirical,
verified work the Blueprint scripted.

**What this reveals about a Blueprint (beyond "a doc"):**
- **Discoverable by intent** — blueprints are catalogued (like cards/skills) so an agent matches "old
  Oracle" → the right guide.
- **It scripts the human-in-the-loop** — *when* to ask the human and *what* to ask for (least-privilege
  creds), with secret-handling.
- **It drives empirical capability discovery** — the agent *probes the live target* (least-priv) to learn
  real capabilities, then announces matching tokens. **Two honesty gates stack:** the agent announces only
  what it probed, and the conformance kit verifies what it announced (over-claim caught either way).
- **It produces "generic-but-proven," not "optimal"** — correctness is proven by the kit; performance
  tuning is a separate, later, human/craft concern (consistent with the §3 boundary).
- **Reuse before build (the framework's own discipline)** — the blueprint's *first* move is "does this
  already exist on NuGet / in the catalogue?" Authoring a new adapter is the fallback, not the reflex.
- **It compounds into an ecosystem flywheel** — every conformance-gated adapter an agent publishes grows
  the NuGet pool, so "check first" succeeds more often over time. And the conformance kit is precisely what
  lets you **trust an adapter you didn't write**: run its kit against *your* instance — green = trust.
  (The same gate that proves an agent-built adapter vets a community-built one.)

The safety story, made concrete: even building the *data* adapter (the highest-blast seam), the agent
operates with **least-privilege** during research and ships only what **passes a real-store,
isolation-fuzzed conformance kit** — green is the trust, not the agent's say-so.

## 3. Two deliverables

**The boundary we hold:** the Blueprint teaches an author *how to build a good adapter* — research,
ingredients, structure, the qualities a good adapter has, testing, gotchas — but it does **not** prescribe
the *optimal/performant implementation* (that's the author's craft), and verification is **black-box
behavioral conformance, never code review.** We guide the build and we prove the behavior; we don't hand
over tuned code or grade the source. Both stay implementation-agnostic (work for an agent- or hand-written
adapter) and durable (don't rot as coding techniques change).

### 3a. The Agentic Blueprint — the per-adapter-type authoring guide

The same way Koan ships **cards** (how to *use* a pillar), it ships an **Agentic Blueprint** per adapter
type (how to *extend* a pillar by authoring an adapter): "How to build a Data adapter to a SQL database,"
"How to build an AI adapter," "How to connect to a legacy database," … Each walks an author (agent or
human) through the lifecycle:

1. **Research** — how to investigate the target (its API, its capability / transaction / query model, its
   limits); what to look for and where to find it.
2. **Ingredients** — the client libraries, dependencies, concepts, and the Koan contract (the
   interface(s) + the capability tokens the adapter may announce — the ARCH-0084 capability model).
3. **Build** — how to approach and structure the adapter, and **what a good adapter has**: the obligations
   — *isolate at the chokepoint; ACID where it claims transactions; push down what it announces; carry the
   ambient context; fail-closed; honor classification.*
4. **Test** — how to verify it: run the surface-validation conformance kit (§3b) against a real instance.
5. **Gotchas** — the known pitfalls for this adapter type / target.

It **empowers** the author; it does **not** dictate the optimal code. (A reference adapter may be included
as an *example* of conformance — not "the one true way.")

### 3b. The surface-validation tools — the objective gate

Tools that exercise the adapter's **observable surface** against a real instance and check it meets the
contract + the Blueprint's obligations — **never reading the implementation.** (§4.) The Blueprint's
*Test* step points here; this is what makes "green = trust" real.

**Prior art:** the Jakarta/Java **TCK (Technology Compatibility Kit)** — "implement the spec, pass the TCK
to claim conformance." The **Agentic Blueprint** is the authoring guide + spec-of-goodness; the
**surface-validation tools** are the TCK.

## 4. The keystone — the capability-driven conformance kit

This is what makes "green = trust" *real* rather than vibes-based codegen. **All validation is black-box:
it probes observable behavior against a real instance — it never reads, lints, or reviews the
implementation.** We verify what the adapter *does*, not how it's written.

- **Capability-flag-driven.** The adapter announces a `CapabilitySet`; the harness runs **exactly** the
  conformance modules that match each flag; an un-announced capability → its module is **skipped** (you're
  never tested on what you don't claim).
- **The "no capability-lies" rule.** A capability token and its conformance module are **co-defined** —
  you cannot introduce `Caps.X` into the framework without shipping `ConformanceModule.X`. Every
  announceable capability has an objective verifier, so **over-claim fails green, structurally.** (Parallel
  to Koan's "no boot-lies" self-reporting principle.)
- **Four validation layers (all behavioral):** **honesty** (does the surface *behave* as each flag
  claims — a behavioral probe, e.g. announce ACID → run a concurrency/atomicity test; not a code check) ·
  **surface** (does every verb work?) · **correctness** (an *oracle* — results checked against a CLR
  reference / cross-adapter convergence, not just "non-erroring") · **isolation + classification** (P7
  tenant-isolation fuzz across *every* verb; carry-tenant; honor `[Phi]`; fail-closed).
- **Real-store only (ARCH-0079).** Runs against a real instance (Testcontainers); fakes structurally
  cannot reveal the claim-vs-reality gap.

It is a **generalization of what Koan already has in pieces**: the FilterConvergence TestKit (the oracle),
ARCH-0079 (real-store integration canon), the DATA-0104 capability-honesty oracle, ARCH-0091 (the
Testcontainers harness), and P7 (the tenant-isolation fuzz).

## 5. Tiering by blast-radius

A misbehaving *vector* adapter degrades search; a misbehaving *data/chokepoint* adapter **leaks tenants.**
Tier the conformance rigor and the human-in-the-loop accordingly:

| Tier | Seams | Gate |
|---|---|---|
| **High-blast** | data, chokepoint, auth | ruthless conformance + isolation fuzz **mandatory** + human sign-off on the agent's output |
| **Medium** | cache, messaging, jobs | conformance + isolation |
| **Low** | vector, blob, media | conformance + functional |

## 6. Three jobs, one artifact

The conformance kit is the *same* artifact that:
1. keeps the **ARCH-0084 capability model honest today** (no capability-lies),
2. is the **v1 tenant-isolation/classification proof** (P7), and
3. is the **acceptance gate** that makes agent-authored adapters trustworthy tomorrow.

Build it once; three payoffs. That coincidence is the strongest argument it's the right primitive.

## 7. Open questions

- **Naming (resolved by the architect).** The *artifact* = the **Agentic Blueprint** — a per-adapter-type
  authoring guide (research → ingredients → build → test → gotchas), parallel to **cards** (cards = how to
  *use* a pillar; blueprints = how to *extend* it). The *verifier* = the **surface-validation tools /
  conformance kit**; the *capability* = **agent-extensible adapters**. Framing settled; the RFC
  sanity-checks the name with frontier models.
- **Human-in-the-loop per blast tier** — where exactly is sign-off mandatory?
- **Capability pass-through** — how does an agent-built adapter announce a *provider-specific* capability
  the framework didn't anticipate (Pinecone-only feature), without a framework change? (The round-3
  capability-extensibility regret item.)
- **Is this its own framework facet?** It is framework-wide; it may warrant standing beside the redesign's
  Facet 4 rather than living under tenancy.

## 8. Relation to tenancy

The tenancy **external-infra delegation seam** (round-3 finding) is the *runtime mechanism* — carry
tenant + classification across a boundary to un-owned infra without failing-open. This brief is the
*authoring mechanism* — how that adapter gets built and proven. Tenancy is the pilot; the capability is
general.
