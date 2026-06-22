---
type: RFC
domain: core
title: "Koan Agent-Extensible Adapters — Request for Feedback (and help us name it)"
audience: [frontier-models, external-architects]
status: open-for-review
last_updated: 2026-06-21
---

# Koan Agent-Extensible Adapters — Request for Feedback (and help us name it)

> **You are being asked to evaluate a capability and help name it.** It emerged while designing
> multi-tenancy for a .NET application framework called Koan, as the structural answer to an adoption
> barrier (below). This document is self-contained. We want your honest, adversarial evaluation — where
> the idea breaks, what its trust model can't cover — **and** your help with naming, which we're
> genuinely stuck on.

---

## 0. What we're asking

1. **Evaluate the capability (§2–§5).** Is "conformance-gated, agent-authored adapters" *sound*? Where
   does it break? What can a green conformance kit still miss? Is the trust model adequate for the
   high-blast-radius seams (the ones that, done wrong, leak data across tenants)?
2. **Name it (§6) — the live ask.** We need a good name for **the artifact** (the per-seam thing an agent
   consumes to build an adapter) and possibly **the capability** as a whole. Our candidates are weak.
   Critique them; propose better.
3. **Prior art.** What existing patterns/systems does this resemble (we know the Jakarta **TCK**; what
   else?), and what can we steal from how they succeeded or failed?
4. **The killer use / delight.** What's the single most compelling thing this unlocks — the "we have to
   have this" moment for a team?

Reply structure: **(a)** soundness + holes · **(b)** the naming (artifact + capability, with reasoning) ·
**(c)** prior art · **(d)** the killer use.

---

## 1. Context — Koan, and the barrier this solves

**Koan** is an entity-first .NET 10 application meta-framework. Its defining trait: it **owns every
backend pillar in one runtime** — data, web, cache, vector search, jobs, messaging, storage, auth, AI,
observability — which lets cross-cutting concerns (like tenant isolation) be *properties of the runtime*
rather than predicates every query must remember. Adapters (for Postgres, Redis, a vector store, a
message bus, …) plug in via "Reference = Intent" (a package reference auto-activates the capability), and
each adapter **announces its capabilities** through a typed capability model; the framework composes
against what's announced.

**The barrier.** That "owns every axis" strength is also its sharpest adoption risk: a team that wants
Koan's tenancy/isolation but is *mandated* by their enterprise to use, say, Pinecone for vectors or an
existing Kafka bus Koan doesn't have an adapter for, faces "rip out your approved stack or walk away."
The supported-adapter set is bounded by what the maintainer had time to write.

---

## 2. The capability

Reframe "agent-native." Most frameworks that use the term mean *agents can use the framework.* Koan's
claim: **the framework can extend itself through agents.** The supported-adapter set becomes bounded not
by maintainer time but by **what an agent can generate and verify on demand:**

```
koan adapter new --seam vector --provider pinecone
  → an agent reads the vector-seam authoring guide  (§3)
  → writes the adapter
  → runs the conformance kit against a real Pinecone instance  (§4)
  → GREEN = shippable; RED = the agent iterates
```

If this works, "owns every axis" becomes "**coordinates** every axis, and grows a new one on demand,"
and the adoption barrier largely dissolves.

---

## 3. The artifact — a per-seam authoring guide *(what we need to name)*

A per-seam, agent-readable bundle of five things:

1. **The contract** — the interface(s) + the capability tokens the adapter may announce.
2. **The invariants** — fail-closed; carry the ambient tenant; honor data classification; **never
   fail-open**. Encoded as checks, not prose.
3. **A reference adapter** — a worked exemplar to pattern-match against.
4. **The conformance kit** — the objective acceptance gate (§4).
5. **Seam-specifics** — e.g. for a vector adapter: how to carry the tenant into the collection namespace,
   how to honor "this field is classified, don't embed it"; for messaging: tenant on the outbox/envelope,
   strip classified fields before durable storage.

We believe this resembles the Jakarta/Java **TCK (Technology Compatibility Kit)** — "implement the spec,
pass the TCK to claim conformance" — combined with an *agent-targeted authoring guide.* Is that the right
mental model?

---

## 4. The keystone — the capability-driven conformance kit (why this isn't vibes-based codegen)

The entire trust model rests here. An agent-generated adapter is only trustworthy because there's an
**objective acceptance gate**, not human approval of plausible-looking code:

- **Capability-flag-driven.** The adapter announces a capability set; the harness runs **exactly** the
  conformance modules matching each flag; un-announced capabilities are **not** tested (you're judged only
  on what you claim).
- **The "no capability-lies" rule.** A capability token and its conformance module are **co-defined** —
  the framework cannot offer `Caps.X` without shipping `ConformanceModule.X`. So **over-claiming a
  capability fails the suite, structurally.**
- **Four validation layers:** **honesty** (does it do what its flags claim?), **surface** (does every
  verb work?), **correctness** (an *oracle* — results compared to a known-correct reference / another
  adapter, not merely "it didn't throw"), and **isolation** (a property-based fuzz that asserts tenant A
  cannot read or write tenant B across *every* verb the adapter exposes; honors classification;
  fail-closed).
- **Real-store only.** Runs against a real instance (containerized), never a fake — because the whole
  point is to catch the gap between what the adapter *claims* and what the real backend *does.*

So the agent doesn't ship code that "seems right"; it ships code that **passes a capability-derived,
oracle-checked, isolation-fuzzed suite against a real instance.** Green is the trust.

**Question for you:** is that gate *sufficient*? What class of defect passes a rigorous conformance kit
and still ships a bug — especially a *security* bug (cross-tenant leak)? Where would you *not* trust an
agent-authored adapter even with a green kit?

---

## 5. Tiering by blast-radius

A misbehaving *vector* adapter degrades search; a misbehaving *data/auth* adapter **leaks tenant data.**
So the gate is tiered:

- **High-blast** (data, the isolation chokepoint, auth): ruthless conformance + mandatory isolation fuzz +
  **human sign-off** on the agent's output.
- **Medium** (cache, messaging, jobs): conformance + isolation.
- **Low** (vector, blob, media): conformance + functional.

Is blast-radius the right axis for tiering trust? Is there a better one?

---

## 6. The naming problem (please help)

We need a name for **the artifact** — the per-seam bundle (contract + invariants + reference +
conformance kit + seam-specifics) that an agent consumes to build an adapter. Existing Koan lexicon uses
*"skills"* (how agents learn to use it) and *"cards"* (API-truth docs), so the new term should slot
beside those without colliding.

Our candidates, and why each is imperfect:

| Candidate | For it | Against it |
|---|---|---|
| **Blueprint** | "a buildable spec" — accurate to the contract half | undersells the proof/conformance half |
| **Playbook** | "step-by-step plays an actor executes" — fits *directing* an agent | undersells the contract + the test gate |
| **Workbook** | implies the agent *does work* guided by it | "fill-in-the-blanks" connotation feels light |
| **Rulebook** | captures the invariants (fail-closed) | undersells the build/proof |
| **Handbook** | a craftsperson's manual; general | generic; doesn't signal the conformance gate |
| **Codex** | authoritative, distinctive | maybe too grand |

The hard part: the artifact is simultaneously **instruction + contract + proof**, and no common word
captures all three. Is there a word (or a coinable Koan term) that does? And separately — is **"agent-
extensible adapters"** the right name for the *capability*, or is there something sharper (the
"self-extending framework," an "adapter forge," …)?

---

## 7. The asks, restated

- **(a)** Is the capability sound? Where does it break? What can a green conformance kit still miss
  (especially a cross-tenant security defect)? Is blast-radius tiering the right trust model?
- **(b)** **Name it** — the artifact (§6) and the capability — with your reasoning.
- **(c)** Prior art beyond the TCK; what to steal.
- **(d)** The killer use — the single most compelling thing this unlocks.

**Be specific and be hard on the trust model — an agent that writes the data adapter is writing the code
that enforces tenant isolation. Thank you.**
