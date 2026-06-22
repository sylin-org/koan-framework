---
type: RFC
domain: core
title: "Koan Agent-Extensible Adapters — Request for Feedback (and help us name it)"
audience: [frontier-models, external-architects]
status: open-for-review
last_updated: 2026-06-21
---

# Koan Agent-Extensible Adapters — Request for Feedback

> **You are being asked to evaluate a capability** that emerged while designing multi-tenancy for a .NET
> application framework called Koan, as the structural answer to an adoption barrier (below). This document
> is self-contained. We want your honest, adversarial evaluation — especially of the trust model. Two
> boundaries we hold: **the *Agentic Blueprint* guides an author on *how to build* a good adapter
> (research, ingredients, structure, the qualities of a good adapter, testing, gotchas) but does not
> prescribe the *optimal/performant code* — that's craft; and verification is *black-box behavioral
> conformance* — we validate what the adapter *does*, never the source.** Tell us where those boundaries
> leak, and whether "verify the behavior, not the code" is a sufficient trust model.

---

## 0. What we're asking

1. **Pressure-test the boundaries (§2–§5).** The **Agentic Blueprint** guides *how to build* a good adapter
   (research → ingredients → structure → qualities → test → gotchas) *without* prescribing the optimal
   code; the **surface validation** checks the behavior meets the bar *without* reading the source. Is that
   the right split? Is there a "good adapter" property that can't be taught as guidance and proven
   black-box?
2. **Stress the trust model.** The validator **never reads the code** — it behaviorally fuzzes the
   *surface* against a real instance (for isolation: can tenant A reach tenant B through *any* verb?). Is
   "verify the behavior, not the code" *sufficient* — especially for the **data adapter, which is the code
   that enforces tenant isolation**? What class of defect passes a rigorous surface-validation and still
   ships a cross-tenant leak?
3. **Prior art.** Beyond the Jakarta **TCK**, what does this resemble, and what should we steal?
4. **The killer use / delight — and a naming sanity-check (§6).** The single most compelling thing this
   unlocks; and we've *landed* on names (the **Agentic Blueprint** = the authoring guide; the
   **surface-validation tools** = the verifier; **agent-extensible adapters** = the capability) — tell us
   if they're wrong.

Reply structure: **(a)** the WHAT/VERIFY/HOW boundary · **(b)** the surface-validation trust model + holes ·
**(c)** prior art · **(d)** killer use + naming sanity-check.

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
  → an agent reads the vector-seam Agentic Blueprint  (§3)
  → writes the adapter
  → runs the conformance kit against a real Pinecone instance  (§4)
  → GREEN = shippable; RED = the agent iterates
```

If this works, "owns every axis" becomes "**coordinates** every axis, and grows a new one on demand,"
and the adoption barrier largely dissolves.

**What it looks like for a developer.** They tell their coding agent: *"Connect to our solution's
database — it's an old Oracle server, I think. Check Koan's blueprints for anything we can use."* The
agent finds the legacy-SQL Blueprint, which scripts the process: **(1)** check NuGet / the catalogue for
an existing adapter first (reuse before build); **(2)** if none, ask the user for a **limited-privilege**
connection string; **(3)** probe the live instance to discover its real capabilities; **(4)** build a
generic adapter announcing only the capabilities it confirmed; **(5)** prove it with the conformance kit
against that instance. The developer said one sentence; the agent did safe, empirical, *verified* work.
(Is that the killer use — or is there a bigger one?)

---

## 3. The two deliverables — the Agentic Blueprint (build) and the surface validation (verify)

We do **not** prescribe "the optimal/performant code" — that's the author's craft. We *guide the build*
and we *verify the behavior*. The author owns the implementation.

**3a. The Agentic Blueprint — a good-implementation-hygiene script.** The Blueprint *set* is a collection
of **good-implementation-hygiene scripts** — one per adapter type — that encode the disciplined process a
good engineer follows (the thing agents most need scaffolded: they write code well but skip the *process*).
Parallel to *cards* (cards = how to *use* a pillar; blueprints = how to *extend* one): "How to build a
Data adapter to a SQL database," "How to build an AI adapter," "How to connect to a legacy database," …
**Two hard constraints:** blueprints are **agent-optimized** (authored for an agent to *execute* —
directive, machine-actionable — not human prose; we target agents) and **vendor-agnostic** (one per
adapter *type* — "a SQL data adapter," "a vector store" — never per vendor; vendor specifics are
*discovered at runtime*, which is *why* the empirical-probe step exists — a blueprint that can't hardcode a
vendor's capabilities must direct the agent to discover them), and **grounded in factual code** — each is
*distilled from Koan's own shipped, conformance-tested adapters* (the real data / OAuth / storage /
messaging adapters), the way cards are derived from real code, not invented. Each scripts the same hygiene:
- **Discover** — find the right blueprint by intent; understand the target; **check NuGet / the catalogue
  for an existing adapter first (reuse before build).**
- **Research** — investigate the target *empirically* (probe a live instance with a limited-privilege
  credential); identify the contract + the capability tokens to announce, and the ingredients.
- **Check online for resources / how-tos** — GitHub, vendor/library docs, registries (reuse *knowledge*).
- **Implement** — a generic, conformant adapter satisfying the obligations: *isolate at the chokepoint;
  ACID where it claims transactions; push down what it announces; carry the ambient context; fail-closed;
  honor classification.*
- **Check for gotchas** — the known pitfalls for this adapter type/target.
- **Test** — run the surface validation (below) against the real instance; green = shippable.

It enforces the *hygiene*; it does not dictate the optimal code. (A reference adapter may be included as an
*example* of conformance — not "the one true way.") The conformance kit is the proof the hygiene was
followed.

**3b. The surface validation — behavioral conformance (§4).** Tools that exercise the adapter's
*observable surface* against a real instance and check it meets the contract + the Blueprint's obligations
— **never reading the implementation.**

This resembles the Jakarta/Java **TCK (Technology Compatibility Kit)** — "implement the spec, pass the TCK
to claim conformance." The Agentic Blueprint is the authoring guide + spec-of-goodness; the surface
validation is the TCK. Is that the right mental model — and is there a "good adapter" property that
*can't* be taught as guidance or verified black-box
or verified black-box?

---

## 4. The keystone — the capability-driven conformance kit (why this isn't vibes-based codegen)

The entire trust model rests here. An agent-generated adapter is only trustworthy because there's an
**objective acceptance gate** — and crucially it is **black-box: it probes observable behavior against a
real instance and never reads, lints, or reviews the implementation.** We verify what the adapter *does*,
not how it's written:

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

## 6. Naming — a sanity-check (we've landed; tell us if we're wrong)

Once we separated the *authoring guide* from the *verifier* (§3), the naming settled:

- **The Agentic Blueprint** — the per-adapter-type authoring guide (research → ingredients → build → test
  → gotchas). It slots beside the existing Koan lexicon: **cards** = how to *use* a pillar; **Agentic
  Blueprints** = how to *extend* a pillar (build an adapter for it). There is one per adapter type ("Data
  adapter to a SQL database," "AI adapter," "legacy database," …).
- **The surface-validation tools** (a.k.a. the conformance kit) — the behavioral verifier.
- **Agent-extensible adapters** — the capability as a whole.

Sanity-check us: does "Agentic Blueprint" read right for a full authoring guide that's parallel to cards?
Is there a sharper name for the *capability* than "agent-extensible adapters"? Low-stakes now — the
*concept* (guide the build, prove the behavior, never the optimal code or a code review) is what we want
validated.

---

## 7. The asks, restated

- **(a)** Are the two boundaries right — the Blueprint **guides the build** (research → ingredients →
  structure → qualities → test → gotchas) but stops short of prescribing the *optimal code*, and the
  verifier **proves the behavior** but never reads the source? Is there a "good adapter" property that
  can't be taught as guidance or verified black-box?
- **(b)** Is **surface validation a sufficient trust model** — does any class of defect pass a rigorous,
  real-store, isolation-fuzzed surface-validation and still ship a cross-tenant leak? Where would you
  *still* not trust an agent-authored data adapter even with a green kit?
- **(c)** Prior art beyond the TCK; what to steal.
- **(d)** The killer use — the single most compelling thing this unlocks — and the naming sanity-check (§6).

**Be specific and be hard on the trust model — an agent that writes the data adapter is writing the code
that enforces tenant isolation. Thank you.**
