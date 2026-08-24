---
id: ARCH-0134
slug: a-capabilities-tree-routes-agent-knowledge-by-intent
domain: Architecture
status: Accepted
date: 2026-08-23
title: A capabilities tree routes agent knowledge by intent
related:
  - CORE-0090
  - ARCH-0041
---

# ARCH-0134: A capabilities tree routes agent knowledge by intent

## Context

Cold-run evaluation of the POC recipe (see `evals/koan/recipe-runs/`) produced a finding bigger
than any single defect: an agent with full network access, holding a correct recipe, still lost
the majority of its session to **capability routing** - discovering which packages exist, which
combine, which constraints bind them, and where the canonical instructions live. The flat
capability map answers the support question ("does Koan support X?") cheaply, but routing an
outcome ("add semantic search") through it costs a 26 KB fetch and returns prose, not decisions.

The request that triggered this ADR proposed the shape directly: a hierarchical catalog -
top-level index, domain pages, capability pages carrying decision guidance, and per-deployment
variant leaves. Example: "add semantic search" routes index → `ai.md` → a capability node that
states the binding constraint (*the same model and dimensions must serve both the stored
embeddings and the query*) and a scale table (portable single exe / local Ollama / hosted
remote), each row a leaf.

Strategic review added three findings. First, fetch-cost is a first-class constraint for agents:
progressive disclosure at the documentation layer mirrors what SKILL.md already does at the skill
layer, and predictable fetch budgets are a preference signal for coding agents choosing between
frameworks. Second, the tree is fetchable at stable URLs by any agent with no Koan installation -
it is zero-install reach, and the existing snapshot mechanism bundles it for offline agents.
Third, a future MCP knowledge server (a standing proposal) stops being a project and becomes a
publisher of this tree.

## Decision

Koan documents capabilities as a four-level tree under `docs/capabilities/`, with strict
separation of concerns per level:

1. `index.md` - the router: one line per domain. Fetched on every routing decision, including
   when the domain seems obvious, so new domains are discovered.
2. `<domain>.md` - the domain node: lists the capabilities users name, routing each to a
   capability node or the current best leaf.
3. `<domain>/<capability>.md` - the capability node: outcome, required packages, the **constraint
   box** (invariants that span steps - e.g., model/dimension pairing), and a decision table of
   variants.
4. `<domain>/<capability>/<variant>.md` - the variant leaf: how to execute one deployment shape,
   plus variant-specific gotchas only.

Rules:

- **Nodes route; leaves teach.** The tree never duplicates leaf content. Leaves are existing
  recipes, guides, and package READMEs.
- **Mechanics live in package READMEs; the tree owns choices and constraints.** A variant leaf
  routes to the connector README (which sits beside the restored package at the version in use)
  and carries only decision context and variant-specific gotchas. Thin-leaf rule: a page with
  fewer than ~10 non-README lines is a table row in the capability node instead.
- **Constraints are hand-written at branch points** and are inherited by fetch order - the
  parent is always read before the leaf, so leaves never restate invariants. Listings may become
  generated later; constraints may not.
- **Nodes are named by the sentence users say** ("semantic search", not "embedding"), with
  cross-links to sibling intents sharing a primitive.
- **Absences are stated.** Local-first means no hosted frontier-model connector ships; the tree
  says so where a row would otherwise imply one. Claim discipline applies with full force.
- `docs/reference/capability-map.md` remains the one-screen summary for humans and cheap fetches,
  cross-linked with the tree; the tree is the deep navigation.

Distribution: the tree is bundled into the koan skill's offline snapshots alongside the map, and
its stable GitHub URLs make every node fetchable by an agent with no Koan installation at all.

## Consequences

- Routing happens at the point of need, in fetch-sized steps, with constraints arriving before
  the code decision instead of after the error.
- The cold-run evaluation loop feeds the tree: every friction log is a candidate constraint box.
  Upkeep of hand-written nodes is the deliberate cost, offset by link linting and the loop itself.
- A future MCP knowledge server publishes this tree as resources rather than defining its own
  model.
- Two capability views now exist (flat map, tree). They share leaf targets and cross-link; if
  drift appears, the map shrinks toward a summary and the tree absorbs the depth.
- Constraint boxes are candidates to graduate into framework diagnostics (frontmatter the build
  or analyzers can read) once the tree stabilizes.

## Amendment (2026-08-23): the node contract

First cold-run review showed the difference between documentation that reads well and
documentation that orients an agent. Prose can be correct and still leave the agent guessing
about fetch order, decision tests, and stop conditions. Every node in the tree therefore carries
four sections, in this order:

1. **Route by need** - a table whose left column is a decision test written in the requester's
   vocabulary ("we outgrew SQLite", "orders belong to customers") and whose right column names
   the exact next fetch. Adjectives are not decision tests.
2. **Standing constraints** - the invariants that span steps, stated as gates (same model and
   dimensions for index and query; adding a store never moves data).
3. **Do not, at this level** - the stop conditions planted where the temptation occurs
   (no pre-created schemas; no auth ceremony at idea stage; no second store without a named
   outcome).
4. **Leaves and exemplars** - the working recipe, the guide, and compiled-sample files that
   cannot drift.

Delight lines survive only when they carry decision content ("the code keeps saying `Recipe`
while everything underneath changes" is the invariance promise, so it stays). Capability nodes
promote the constraint box above the route table; domain nodes lead with it. The contract is the
template for all future nodes and for revisions of existing ones.
