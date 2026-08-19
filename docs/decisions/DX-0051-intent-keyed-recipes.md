---
id: DX-0051
title: Recipes keyed on intent, with a generated index
status: Accepted
date: 2026-08-19
area: DX / AX
related: [DX-0049, DX-0050, ARCH-0118]
---

# DX-0051 — Recipes keyed on intent, with a generated index

## Outcome

Koan's agent-facing guidance is keyed on **what a person asks for**, not on what Koan publishes.
Exactly two layers:

| Layer | What it is | How it is produced |
|---|---|---|
| **Index** | one fetch; every recipe's result, precondition, operating cost, and needs | **generated** from recipe frontmatter |
| **Recipe** | one sentence a person would say, with one coherent proof | hand-written |

The agent composes the answer to a vague request *from the index*. There are no hand-written scenario
pages, because a scenario page is a stale cache of something the model derives better per
conversation — and cannot say "you already have Entities carrying text, so this is a small step."

Package identifiers are the last mile of a recipe, never its organizing principle.

## Context

The capability map (DX-0050) is keyed on packages, with outcomes as row labels. That is the right
shape for a *named* request — "add Mongo" — and the wrong shape for the request that actually arrives:
"what if I added AI to this?" That sentence could mean semantic search, question answering, vision, an
acting agent, or human review of model output: five projects, different runtimes, different operating
costs. Matching it to a row and naming a package picks one of five and is usually wrong.

The first attempt at a fix was a hand-written scenario page that enumerated the five and routed between
them. It was wrong for three reasons, and the reasons generalize: it duplicated what the index could
carry, it could not observe the application in front of it, and no one would ever write the page for
"add AI *and* isolate tenants."

Three nouns had been conflated, and only one of them is ever spoken aloud:

- **Recipe** — a unit of user intent ("let people sign in"). The human says this.
- **Capability** — a unit of framework function (authentication). The framework says this.
- **Package** — a unit of publication. NuGet says this.

The relation is many-to-many at both hops.

## Decision

### Two layers, and only two

The index answers "what could I do here?" in one fetch. A recipe answers "how do I do that one?" in
the second. A third routing layer is not added: an LLM reading forty well-described entries routes
better than any taxonomy, and a taxonomy is a second artifact to maintain and to get wrong. The index
stays flat.

### The index is generated, never written

`scripts/build-recipe-index.ps1` compiles `docs/recipes/index.md` from recipe frontmatter, and
`-Check` fails on drift. A derived artifact that is hand-maintained is a lie with a timestamp.

The schema stays flat — scalars and lists of delimited strings — so the generator needs no YAML
dependency on a build agent, and so a recipe author is never fighting indentation.

### A recipe is one sentence, with one proof

Granularity is settled by the proof, because the proof is objective. "Answer questions from my docs"
and "summarize this document" prove differently, so they are two recipes; searching Articles and
searching Products prove identically, so they are one.

The split signal is mechanical, so it never has to be argued in review: **when a recipe's Proof section
grows a second "if instead you…" branch, it is two recipes.** Start coarse and let the proof divide it.

### Ingredients are typed, but thin

An ingredient carries a cardinality (`one`, `one-or-more`, `optional`), a human label, and the package
identifiers that satisfy it. The dividing rule for everything else: **if a gate can check it, it is
frontmatter; if only a human can judge it, it is prose.** Cardinality and package identity are
checkable. Which store suits a customer's environment is not, and lives in the body.

Thin matters as much as typed. The dominant failure mode for this architecture is not an inaccurate
recipe — it is that only six recipes ever get written because the format is tedious.

### Axes, not decision trees

A recipe never encodes *which* option to choose. It encodes the axes that discriminate between
them — embedded versus server process, single node versus clustered, whether a container is available,
existing operational investment, data residency — annotated honestly per option, and lets the agent
match those against what the developer actually said.

A decision tree can only answer situations its author imagined. Axes let the model answer the ones
nobody imagined, which is the entire reason a model is in the loop.

### Operating cost is a required field

Not price — what it costs to *run*: whether it works offline, whether it adds a process to operate,
whether it adds a credential to rotate. It is the most-omitted fact in framework documentation and
among the first things a developer actually weighs, so it is a required field rather than an optional
aside.

### Absent ingredients are published

A recipe may declare an ingredient absent, and must pair it with the honest alternative for today.
Two things follow: an agent says it in the first minute instead of the third hour, and the set of
absent ingredients across all recipes is a roadmap that is queryable rather than folklore.

A framework is not rejected for a gap that is stated. It is abandoned for a gap discovered after
adoption, by a user who then tells other people.

### The skill owns technique; recipes own content

Reading the application before offering options, stating an assumption instead of interrogating,
naming the operating cost, offering the thing the developer needs but did not ask for — that is
domain-independent conversational technique and belongs to the skill. It is also the part an index
genuinely cannot carry.

### Interactions are prose, and are not complete

Two recipes can interact in ways neither states — tenant-scoped embeddings crossing an async hop need
the durable ambient carrier, which this repository learned by running a dogfood application rather
than from any document. Recipes carry a short prose *Interacts with* note where a non-obvious
interaction is known.

An interaction graph is deliberately not built. Most pairs are uninteresting, the matrix would be
mostly empty, and its maintenance cost would be paid on every new recipe. This is an accepted
limitation, not an oversight: the agent will not always spot an interaction nobody wrote down.

## Consequences

- A vague request gets a specific, application-aware answer from one fetch.
- Shipping a capability adds a recipe; it never edits a routing page, because there are none.
- The capability map keeps its job — the package-facing index for named requests — and loses the job
  it was bad at.
- Recipes must be written by people who know the axes. This is real editorial cost and the throughput
  limit on the whole architecture.
- The index is only as good as `works_if` and `costs`. A lazy precondition produces confident, wrong
  recommendations, which is worse than a missing recipe.
- `docs/guides/scenarios/add-ai.md` is superseded and removed; its content is redistributed to
  recipes (traps), the skill (technique), and the index (options).

## Alternatives considered

- **Keep the package-keyed map as the single index.** Rejected: it cannot answer the request that
  actually arrives, and bolting outcome labels onto package rows hides that it cannot.
- **Hand-written scenario pages per domain.** Rejected: a stale cache of derivable content, blind to
  the application in front of it, and never written for combinations.
- **A recipe taxonomy or category tree.** Rejected: the model routes better on descriptions, and the
  tree is a second thing to maintain.
- **Full YAML ingredient objects.** Rejected: needs a YAML dependency on build agents and raises the
  cost of writing a recipe, which is the resource in shortest supply.
- **An interaction graph.** Rejected on maintenance cost against a mostly-empty matrix.

## Prior art and evidence

- [llms.txt](https://llmstxt.org) — a retrieval map read at question time rather than embedded.
- [AGENTS.md](https://agents.md) — the harness-neutral instruction file DX-0050 adopts, whose floor
  this decision routes into.
