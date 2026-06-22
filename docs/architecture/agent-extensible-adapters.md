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
  → an agent reads the vector-seam authoring guide
  → writes the adapter
  → runs the conformance kit against a real Pinecone instance
  → GREEN = shippable
```

The lock-in evaporates, and the thesis softens from "**owns** every axis" to "**coordinates** every
axis, and can grow a new one on demand." It also loosens the otherwise greenfield-only go-to-market.

## 3. The artifact — a per-seam adapter authoring guide  *(name: open — see §7)*

A per-seam, agent-readable bundle of:

1. **The contract** — the interface(s) + the capability tokens the adapter *may* announce (the ARCH-0084
   capability model).
2. **The invariants** — fail-closed; carry the ambient tenant; honor classification; **never fail-open**.
   Non-negotiable, encoded, not prose.
3. **A reference adapter** — a worked exemplar ("yours should look structurally like this").
4. **The conformance kit** — the objective acceptance gate (§4).
5. **Seam-specifics** — vector: carry tenant into the collection namespace, honor `[Phi]` exclusion;
   messaging: `TenantId` on the outbox/envelope, strip classified fields; data: the chokepoint
   read-filter + write-guard contract; etc.

**Prior art:** the Jakarta/Java **TCK (Technology Compatibility Kit)** — "implement the spec, pass the TCK
to claim conformance." This is, in effect, *a TCK plus an agent-targeted authoring guide.*

## 4. The keystone — the capability-driven conformance kit

This is what makes "green = trust" *real* rather than vibes-based codegen.

- **Capability-flag-driven.** The adapter announces a `CapabilitySet`; the harness runs **exactly** the
  conformance modules that match each flag; an un-announced capability → its module is **skipped** (you're
  never tested on what you don't claim).
- **The "no capability-lies" rule.** A capability token and its conformance module are **co-defined** —
  you cannot introduce `Caps.X` into the framework without shipping `ConformanceModule.X`. Every
  announceable capability has an objective verifier, so **over-claim fails green, structurally.** (Parallel
  to Koan's "no boot-lies" self-reporting principle.)
- **Four validation layers:** **honesty** (does it do what its flags claim?) · **surface** (does every
  verb work?) · **correctness** (an *oracle* — results checked against a CLR reference / cross-adapter
  convergence, not just "non-erroring") · **isolation + classification** (P7 tenant-isolation fuzz across
  *every* verb; carry-tenant; honor `[Phi]`; fail-closed).
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

- **Naming (the live ask).** The *artifact* — Blueprint / Playbook / Workbook / Rulebook / Handbook /
  Codex? The *capability* — "agent-extensible adapters" / "self-extending framework" / a codename?
  Posed to frontier models in the RFC.
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
