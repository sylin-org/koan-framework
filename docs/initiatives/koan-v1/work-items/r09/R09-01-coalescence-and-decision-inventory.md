---
type: SPEC
domain: framework
title: "R09-01 - Inventory and Decide the Semantic Composition Kernel"
audience: [architects, maintainers, developers, ai-agents]
status: passed
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: focused discovery, mass decision inventory, coalescence assessment, and exact implementation placement
---

# R09-01 — Inventory and decide the Semantic Composition Kernel

- Tranche: `T7A — semantic composition prerequisite`
- Status: `passed`
- Depends on: ARCH-0115 and R07
- Unlocks: the first production implementation slice of R09
- Owner: architecture discovery and exact decision placement; no production runtime change

## Meaningful outcome

A maintainer or coding agent can identify where every current composition decision lives, why it
exists, what it costs, which sibling mechanisms duplicate its mechanics, and the one target owner that
will replace or retain it. The first implementation card can then build a meaningful vertical result
without inventing architecture while editing production code.

## Why now

The generic contribution direction is approved, but current Koan has several independently evolved
registries, builders, selectors, facts contributors, and context pipelines. Copying the closest
pattern would preserve accidental ownership. This slice interrogates all candidates before choosing
types, projects, or migration order.

## Evidence to read first

- Decisions: ARCH-0084, ARCH-0105, ARCH-0106, ARCH-0111, ARCH-0113, ARCH-0114, and ARCH-0115.
- Core: generated registry/direct-reference manifest, composition snapshot/builder, runtime facts,
  capabilities, module lifecycle, context carrier registry, and service discovery coordinator.
- Pillars: Data axes/AODB/adapter resolution; Cache plans/key scope/coherence; Communication router,
  descriptors, ingress, and facts; Jobs context hops; Storage key scoping.
- Capabilities/adapters: Tenancy, Access, SoftDelete, ZenGarden, Mongo, Redis, RabbitMQ, and at least one
  local/default provider per concern.
- Tests: multi-host composition, Data axes/no-leak, Cache eviction/coherence, Communication local and
  RabbitMQ, layered discovery, direct-reference intent, runtime facts, and package activation.

## Focused discovery and coalescence assessment

For each logical block, record:

1. the user's business sentence;
2. the smallest honest C# expression and complete action surface—references, decorations,
   configuration, context, and runtime prerequisites—plus its exact guarantee and corrective failure;
3. every additional public concept and why the business or guarantee requires it;
4. current decision owner, consumers, state lifetime, and hot-path cost;
5. repeated mechanics elsewhere in the repository;
6. candidate owner at framework, family, pillar, adapter, or application specificity;
7. keep, absorb, rebuild, or delete disposition;
8. state/cache/compilation strategy;
9. developer, coding-agent, operator, and reviewer experience;
10. facts, health, error, and semantic-diff projection;
11. exact red proof and deletion list; and
12. evidence that would stop or redirect the design.

The closest existing pattern is evidence, not authority.

## Decisions

### DECIDED

- The inventory is mass and cross-pillar; a Data-only or Communication-only design is insufficient.
- Shared mechanics and typed policy are evaluated separately.
- One generic contribution compiler must express optional ZenGarden and hard Tenancy without owning
  either fallback posture.
- Runtime plans are host-owned and immutable; ambient values bind during execution, not compilation.
- Inactive compatibility declarations are structurally inert and preferably uninstantiated.
- Current public behavior is not preserved when it requires duplicate decision owners.
- The `AddKoan` / `Entity<T>` / `EntityController<T>` mapping is the ergonomics benchmark: public
  semantics precede internal types, and the common path cannot expose contribution machinery.
- No production code begins until this card records exact target placement and red tests.

### DEFAULT

- Use current `KoanRegistry`/direct-reference evidence as activation input rather than building another
  package scanner.
- Use current runtime facts as the projection spine rather than building another diagnostic store.
- Reuse current pillar builders only when their state ownership and collision rules survive the
  multi-host/AOT review.

### CLOSED BY THIS ASSESSMENT

- Exact Core contract family and `src/Koan.Core/Semantics` placement are recorded in the inventory.
- Cross-pillar segmentation compiles as a capability-family model; earned pillar-specific contracts stay
  inert and bounded. Tenancy does not reference every pillar implementation.
- The semantic catalog owns declared/active/satisfied meaning, typed immutable plans own execution,
  `koan.lock.json` remains build intent, and resolved diff/facts project the canonical model.
- Data and Communication share identity, activation, qualification outcome, deterministic
  order/collision, and decision receipts—not one fixed selector/fallback policy.
- Cache and Storage become the owners of cache/storage segmentation and stop reading Data registries.
- Finite plans compile eagerly; legitimate dynamic structural shapes memoize once per host; ambient
  values remain runtime inputs.
- Stable decision/problem/correction identities and bounded model diff are required for V1; the neutral
  operation model and broad workbench remain V1.1.
- The remaining bundle-export representation and measured performance budget are implementation
  questions explicitly owned by R09-02, not unresolved architecture.

## Scope

### In

- Update [R09-COALESCENCE-INVENTORY.md](../../R09-COALESCENCE-INVENTORY.md) with exact source and test anchors.
- Produce a dependency and decision graph for all candidate owners.
- Measure or structurally prove current discovery, reflection, DI enumeration, allocation, registry,
  and memoization behavior at representative hot paths.
- Decide exact contracts, project locations, lifetimes, ordering/collision behavior, activation state,
  problem/fact identities, and plan-cache keys.
- Define red tests for optional and hard contribution archetypes.
- Produce the dependency-ordered implementation/deletion sequence and create only the next child card.
- Record small discovered defects in `POST-CYCLE-TODO.md` unless they invalidate this architecture.

### Out

- Production implementation of the contribution compiler or semantic host plan.
- Opportunistic fixes to every inventoried mechanism.
- Public API compatibility work, package publication, release certification, or remote mutation.
- Full design of the V1.1 neutral operation model.

## Initial findings

- ARCH-0114 already establishes inert declaration, Reference = Intent activation, concern-owned
  election, adapter realization, and `declared`/`active`/`selected` evidence.
- Data axes prove accumulative typed declaration and smart expansion but retain process-static state
  and leak Data-owned scope knowledge into Cache and Storage.
- Core context carriers prove a generic cross-pillar mechanism with module-owned meaning and explicit
  ingress provenance.
- Communication already has one immutable host route plan and distinguishes hard capabilities from
  delivery assurance, but its selection mechanics are pillar-local.
- Entity Cache has one strong policy/key/eviction plan, while generic Cache has no automatic tenant
  partition.
- Runtime facts already serve startup, Web, and MCP, but full rejected-candidate and cross-pillar
  guarantee coverage is not canonical.
- Explicit Mongo ZenGarden intent currently falls back when the engine is absent/unresolved; this is
  a bounded semantic-honesty repair, not permission to redesign Mongo inside this assessment.

## Execution plan

1. Complete the decision-owner/type/project inventory and dependency graph.
2. Inventory constants, options, descriptors, shared DTOs/contracts, and generated discovery inputs;
   identify what already exists versus what must be created.
3. Trace one optional layer end to end: Mongo/ZenGarden declaration through candidate selection,
   health, fallback, facts, and removal.
4. Trace one hard overlay end to end: Tenancy through Data, Entity/generic Cache, Storage, Jobs,
   local/RabbitMQ Communication, ingress trust, facts, and no-leak proofs.
5. Compare Data and Communication provider decision tables and identify the exact shared mechanics.
6. Define semantic catalog/host plan/information-plane boundaries and stable identities.
7. Define the typed contributor compiler, activation filter, fold ownership, lifetime, AOT shape, and
   corrective failure contract.
8. Define red tests, performance probes, package/dependency placement, and the deletion sequence.
9. Amend ARCH-0115 only where evidence changes the accepted law; create the next child card.

## Verification

- Documentation-only kickoff: strict docs, link validation, `git diff --check`, and privacy sweep.
- Structural searches: every `Contributor`, `Registry`, `Resolver`, `Coordinator`, `Plan`, provider
  priority, capability requirement, and composition/facts owner in the scoped pillars is classified.
- Dependency evidence: evaluated project references confirm proposed layering is acyclic and direct
  intent cannot be inferred from a transitive contracts reference.
- Performance baseline: named representative operations identify whether discovery/reflection/DI
  enumeration occurs at boot, first structural use, or every operation.
- Test map: every load-bearing claim links to an existing test or an explicitly red future cell.
- No release certification or public mutation.

## Acceptance additions

- No production type is proposed without exact location and why the next narrower/wider owner is wrong.
- The target contains one generic mechanics kernel and typed pillar policy, not a universal pipeline.
- The optional and hard archetypes have complete, contradictory failure-policy tests.
- Every current mechanism has a disposition and deletion/migration order.
- The first implementation child yields a business/operational result in the same card as the new
  abstraction.
- Every proposed public expression is evaluated for business-to-code density, IntelliSense discovery,
  human readability, and coding-model legibility before its internal design is accepted.
- The inventory states current unsupported Cache and Communication segmentation levels exactly.

## Stop conditions

- Stop if exact source evidence contradicts ARCH-0115's ownership law; amend the decision before code.
- Stop if typed contribution requires Core to reference pillars or an optional capability reference to
  activate all target pillars transitively.
- Stop if a shared selector cannot express a current Data or Communication hard requirement without
  special-casing the pillar.
- Stop if the plan requires mutable process-wide runtime state or per-operation compilation.
- Stop before production code, broad certification, publication, push, tag, release, or private
  downstream inspection.

## Kickoff record

- Date: 2026-07-16
- Initial assessment: complete enough to establish ARCH-0115, the owner map, the Tenancy coverage
  matrix, the ZenGarden/Tenancy counterexample pair, and the exact remaining discovery questions.
- Repository boundary: branch `dev`, starting HEAD `546817ee0d3a`; preserve the five intentional
  R08 companion-document modifications and never stage `tmp/`.
- Next safe action: execute
  [R09-02](R09-02-host-constitution-and-semantic-activation.md), beginning with its red activation and
  build-consumer fixtures.

## Acceptance result

- Passed on 2026-07-16 as a documentation/architecture slice; no production runtime files changed.
- The [coalescence inventory](../../R09-COALESCENCE-INVENTORY.md) now contains the complete application
  action surfaces, dependency/activation graph, source/type/lifetime/hot-path owner map, exact
  keep/absorb/rebuild/delete dispositions, Data/Communication counterexample, official prior-art
  assessment, target contracts/placement, capability-family decision, host lifecycle, red matrix, and
  deletion order.
- ARCH-0115 was refined from a literal Tenancy-to-every-pillar overload example to one segmentation
  family contribution plus pillar-owned typed realizations; the generic contribution law itself is
  unchanged.
- Structural evidence identified every boot/first-use/hot-path reflection, Activator, DI enumeration,
  process-static registry, and memoization owner in scope. Executable numerical probes are intentionally
  paired with R09-02's red tests so they become ratchets rather than prose measurements.
- Focused docs/links/diff/privacy validation completed; no broad release certification, publication,
  remote mutation, or private downstream inspection occurred.
- Validation: targeted initiative/ARCH-0115 lint completed with `0` errors and `96` historical/schema
  warnings; explore-skill lint completed with `0` errors and `0` warnings; `git diff --check` is clean
  apart from line-ending notices; the private-name/path sweep returned no matches.
