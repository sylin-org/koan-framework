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

### 3a. The Agentic Blueprint — a good-implementation-hygiene script

The Blueprint *set* is a collection of **good-implementation-hygiene scripts** — one per adapter type —
that encode the disciplined process a good engineer follows. That is precisely what an agent most needs
scaffolded: agents write code fluently but skip the *process* (they don't check what already exists, don't
research, don't hunt for gotchas, don't test). The Blueprint enforces the hygiene; the conformance kit
proves it was followed (you can't fake green). Parallel to **cards** (cards = how to *use* a pillar;
blueprints = how to *extend* one) — **one per adapter type across every extensible pillar**: "How to build
a Data adapter to a SQL database," "How to implement an OAuth adapter," "How to implement a storage
adapter," "How to implement a messaging-queue adapter," "How to build an AI adapter," "How to connect to a
legacy database," … 

**Two hard constraints on every blueprint:**
- **Agent-optimized, not human-facing.** Blueprints are authored *for an agent to execute* — directive,
  structured, machine-actionable — not narrative prose for a human reader. We are targeting agents. (A
  human can still read one, but the agent is the audience.)
- **Vendor-agnostic.** A blueprint is **per adapter *type*** ("a SQL data adapter," "a vector store," "a
  message bus," "an AI provider") — **never per vendor.** Vendor specifics (Oracle's quirks, Pinecone's
  API) are **discovered at runtime, not encoded.** This keeps the set small and durable (a new vendor
  never needs a new blueprint) — and it *structurally requires* the empirical-probe model: a blueprint
  that can't hardcode a vendor's capabilities **must** direct the agent to discover them.
- **Grounded in factual first-party code.** A blueprint is **not invented theory — it is distilled from
  Koan's own shipped, conformance-tested adapters** for that pillar (the real Postgres/SQL Server data
  adapters, the RabbitMQ messaging adapter, the OAuth connectors, the storage profiles, …). The
  vendor-agnostic obligations, patterns, and gotchas are extracted from what our *proven* adapters
  actually do; a first-party adapter is the worked example. Like **cards** (API-truth derived from real
  code), blueprints stay factual and checkable against the source — they can't drift into fiction.

Each scripts the same hygiene:

1. **Discover** — find the right blueprint by intent; understand the target; **check NuGet / the catalogue
   for an existing adapter first (reuse before build).**
2. **Research** — investigate the target *empirically* (probe a live instance with a limited-privilege
   credential to learn its real capability / transaction / query model); identify the contract + the
   capability tokens to announce, and the ingredients (client libs, dependencies).
3. **Check online for resources / how-tos** — search GitHub, vendor/library docs, and registries for
   examples and driver guides (reuse *knowledge*, don't reinvent).
4. **Implement** — build a generic, conformant adapter that satisfies the obligations (*what a good adapter
   has*: isolate at the chokepoint; ACID where it claims transactions; push down what it announces; carry
   the ambient context; fail-closed; honor classification).
5. **Check for gotchas** — review against the known pitfalls for this adapter type / target.
6. **Test** — run the surface-validation conformance kit (§3b) against the real instance; green = shippable.

It **empowers** the author and enforces the *hygiene*; it does **not** dictate the optimal code. (A
reference adapter may be included as an *example* of conformance — not "the one true way.")

### 3b. The surface-validation tools — the objective gate

Tools that exercise the adapter's **observable surface** against a real instance and check it meets the
contract + the Blueprint's obligations — **black-box-first** (for high-blast seams, augmented by a narrow
static lint + a review of the isolation-critical lines; §5). The Blueprint's *Test* step points here; this
is what makes "green = trust" real.

**Prior art:** the Jakarta/Java **TCK (Technology Compatibility Kit)** — "implement the spec, pass the TCK
to claim conformance." The **Agentic Blueprint** is the authoring guide + spec-of-goodness; the
**surface-validation tools** are the TCK.

## 4. The keystone — the capability-driven conformance kit

This is what makes "green = trust" *real* rather than vibes-based codegen. Validation is
**black-box-first** — it probes observable behavior against a real instance, verifying what the adapter
*does*, not how it's written. *(External review corrected an over-pure version of this claim: for
high-blast seams black-box alone is **not sufficient** — a narrow static lint + an isolation-line review
are added as defense-in-depth; see §5.)*

- **Capability-flag-driven.** The adapter announces a `CapabilitySet`; the harness runs **exactly** the
  conformance modules that match each flag; an un-announced capability → its module is **skipped** (you're
  never tested on what you don't claim).
- **The "no capability-lies" rule.** A capability token and its conformance module are **co-defined** —
  you cannot introduce `Caps.X` into the framework without shipping `ConformanceModule.X`. Every
  announceable capability has an objective verifier, so **over-claim fails green, structurally.** (Parallel
  to Koan's "no boot-lies" self-reporting principle.)
- **The behavioral layers** (all black-box, against a real instance): **honesty** (does the surface
  *behave* as each flag claims?) · **surface** (every verb works?) · **correctness** (an *oracle* — results
  vs a CLR reference / cross-adapter convergence) · **isolation + classification** (tenant-isolation fuzz
  across *every* verb **including raw/bulk paths**; carry-tenant; honor `[Phi]`; fail-closed; **error
  messages leak no cross-tenant identifiers**).
- **Beyond the happy path** (round-1 review additions — still black-box, but harder): **contention**
  (saturate the pool, N concurrent workers × M tenants → catch connection-state carryover / session
  poisoning) · **soak** (N-thousand ops, measure the process's handle/connection/memory footprint → catch
  resource leaks) · **chaos / fault-injection** (a Toxiproxy/Jepsen-style proxy drops/delays/severs calls →
  prove the adapter fails **closed**, never open) · **durability/restart** (restart mid-run → catch the
  "in-memory shim" that returns correct rows but persists nothing). The harness **reuses one adapter
  instance across all tenants** (mimics the production singleton) — a fresh-per-test harness can't catch
  instance-state leaks.
- **Bias to strictness** (Rust Miri's stance): a green kit must *mean* something. False positives (a
  correct adapter fails on brittleness) are annoying; false negatives (a broken adapter passes) are
  catastrophic. Tune toward strict.
- **Real-store only (ARCH-0079).** Runs against a real instance (Testcontainers); fakes structurally
  cannot reveal the claim-vs-reality gap.

It is a **generalization of what Koan already has in pieces**: the FilterConvergence TestKit (the oracle),
ARCH-0079 (real-store integration canon), the DATA-0104 capability-honesty oracle, ARCH-0091 (the
Testcontainers harness), and P7 (the tenant-isolation fuzz).

## 5. Tiering — by the data classification carried, not the infrastructure category

**External review's sharpest correction:** "vector = low blast-radius" was *wrong.* A vector adapter
carrying `[Phi]` fields (`Embeddable = true`) can leak medical records into another tenant's RAG context —
cross-tenant prompt-injection / exfiltration. **An adapter's blast-radius is the highest data-classification
tier permitted to ride its capability tokens — never its infrastructure category.** Tiering is therefore
*dynamic*: the same vector adapter is low-blast carrying public data, high-blast carrying PHI. This ties
the trust model directly to the classification axis (blast-radius *is* the classification posture).

| Blast (by data carried) | Gate |
|---|---|
| **High** (PHI/PII/PCI/Secret rides the adapter) | the full behavioral suite **+ contention + soak + chaos + durability** · **a narrow static lint** (a Roslyn/AST denylist of structurally-dangerous patterns — `static` mutable state, in-memory data shims, unmanaged threads, missing connection-lifecycle hooks, raw-error passthrough; the eBPF-verifier model — *not* a correctness review) · **human diff-review of only the isolation-critical lines** (tenant-predicate injection + connection lifecycle) |
| **Medium** (internal / low-sensitivity) | the full behavioral suite + contention |
| **Low** (public) | the behavioral suite |

**The two boundaries now decouple.** We still **never prescribe the optimal code** (that's craft) — but
for high-blast we **do read it, *narrowly*** — a *forbidden-pattern* denylist, not a correctness grade.
Black-box behavior stays the primary gate; the lint + isolation-line review are **defense-in-depth**,
because the residual error-path / async-context-race risk is real and no black-box test fully automates it.
(Koan's own `EntityContext` restore-on-scope-exit bounds any such leak to a *single* mis-routed op, never
persistent — and the kit tests exactly that: trigger a fault inside an adapter call, assert the *next*
call's tenant context is correct.)

## 6. Three jobs, one artifact

The conformance kit is the *same* artifact that:
1. keeps the **ARCH-0084 capability model honest today** (no capability-lies),
2. is the **v1 tenant-isolation/classification proof** (P7), and
3. is the **acceptance gate** that makes agent-authored adapters trustworthy tomorrow.

Build it once; three payoffs. That coincidence is the strongest argument it's the right primitive.

## 6a. External review (round 1) — adopted corrections

Three frontier models reviewed the RFC. They validated the boundaries, the vendor-agnostic empirical-probe
design, the killer use, and the name — and **corrected the trust model from "black-box-only" to
defense-in-depth tiered by data-classification** (folded into §4–§5). The rest:

- **Maturity lifecycle (from Dapr's component model).** A green kit is binary; trust is earned over time.
  Tier adapters by maturity, not just blast-radius: **conformant** (passes the kit) → **proven** (N months
  in production, no incident) → **certified** (human review + soak + lint). A signal beyond pass/fail.
- **Version-binding + fleet regression (from JDBC certification / Rust Crater).** A green result is bound
  to *(framework version, provider version)*; on a framework or provider change, **re-run the kit across
  every adapter** (Crater-style) to detect drift. The result records the versions it was run against.
- **Governance of generated code = regenerate, don't hand-maintain.** The generated adapter is deposited
  in the consumer's source control (for same-DX), but it is a **regenerable build artifact**, not
  hand-maintained code. When a provider ships a breaking wire-protocol change: **re-run the Blueprint →
  regenerate → re-verify** against the (unchanged) kit. The Blueprint + kit are the durable assets; the
  adapter is their output. This answers "how do we maintain agent-written code at day-2."
- **Negative-case + reject-illegal testing (from K8s CSI sanity tests).** The kit must verify the adapter
  *rejects* illegal operations, not only that it *accepts* legal ones.
- **Naming feedback.** All three flagged **"agentic"** as trend-prone (it may date the name). Two
  recommend dropping it → **"Adapter Blueprint"** (the agent-optimization is a property of *how it's
  written*, not the name; a human following the same script gets the same result); one judged "Agentic
  Blueprint" stellar. The *verifier* name "surface-validation tools" was unanimously clunky → **Conformance
  Kit / Conformance Gate**. The *capability* "agent-extensible adapters" → punchier as **the Adapter
  Forge**. **Open for the architect** (low-stakes; concept is settled).
- **Killer use, sharpened — the *procurement flip* / *substrate hot-swap*.** Not "connect to Oracle" but
  "connect to *our* Oracle" (the weird schema, no-DDL DBA — only the empirical probe handles *this*
  instance). The business framing: an enterprise demands "your data in *our* Azure tenant on Cosmos DB +
  Service Bus" — normally a 9-month rewrite; with the Forge it's *"give us 24 hours"* (generate → green kit
  → human-review the isolation lines → deploy). And when a vendor changes licensing (Redis → Garnet),
  `koan adapter new --seam cache --provider Garnet` hot-swaps the fleet by config. "We don't support your
  infrastructure" stops being a deal-killer.

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
