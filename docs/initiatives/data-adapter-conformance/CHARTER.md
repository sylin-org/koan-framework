---
type: SPEC
domain: data
title: "Data Adapter Conformance Session Charter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: binding mission, authority, invariants, and session protocol
---

# Data Adapter Conformance Session Charter

Every work-item session reads this file in full before acting.

## Mission

Make Koan.Data and every Data adapter faithfully, efficiently, and explainably realize the application decisions
defined by the [Data Adapter Development Primer](../../architecture/data-adapter-development-primer.md).

## Authority

1. The primer, including human-ratified annexes, defines required semantics and the only stable acceptance-ID catalog.
2. A named human-approved design gate—DAC-02 for Data or DAC-49 for the Vector annex—may freeze illustrative syntax
   into a public API or amend an ambiguity before its implementation card begins.
3. Source and tests establish observed behavior, never desired behavior.
4. Existing provider-promotion evidence counts only when it satisfies the exact primer predicate and evidence kind.

Implementation cards do not edit the primer to make a failing implementation pass. A semantic change requires its
own design decision and human approval before implementation resumes.

## Binding invariants

1. **Adherence is claim-relative, not parity-relative.** Every claim passes; every decline fails closed.
2. **Framework first.** Data owns policy, plans, orchestration, common results, and failure semantics. A Family owns
   repeated mechanics. An Adapter owns native translation and execution.
3. **No local framework repair.** A Framework RED cannot close through an adapter workaround.
4. **Gold is a greenfield replacement.** SQLite and MongoDB become references only after their current adapter
   implementations are harvested for facts, removed from the authoring baseline, replaced from empty implementations,
   and independently certified. Framework and Family contracts are reusable; legacy adapter code, structure, helpers,
   control flow, tests, and compatibility paths are not implementation inputs.
5. **Least meaningful moving parts.** A type, cache, registry, resource owner, or abstraction exists only to own a
   necessary contract, remove mechanics with identical meaning/lifetime, or measurably improve a hot path.
6. **Real providers decide LIVE.** A mock, skipped fixture, code inspection, or self-report cannot satisfy LIVE.
7. **One executable contract.** Forge/TestKit projects the primer IDs; it never defines competing obligations.
8. **One claim declaration.** Prefer generation/projection over parallel hand-maintained manifests.
9. **Auditors do not fix. Certifiers do not remediate.** Findings are frozen before implementation begins.
10. **Performance is provider-relative evidence.** Preserve allocations, dispatch counts, native plans, and warm-path
   baselines for the pinned provider/version/fixture; do not impose universal latency parity.
11. **No hidden fallback.** A scan, client-side page, replay, flattening, policy bypass, or swallowed failure is
    observable and invalidates any stronger claim.
12. **Warm paths consume immutable plans.** Structural discovery, reflection, mapping compilation, capability
    negotiation, and readiness work stay off the warm operation.
13. **Repository reality is re-derived.** The roster and prerequisites are checked from the current tree at each card.
14. **Claim scope has authority.** Auditors derive Observed/Advertised truth but cannot narrow it. A human product owner
    approves Target/Declined choices, withdrawals, maturity downgrades, and non-shipping dispositions. A false current
    claim remains RED until an approved change is evaluated under a new identity.
15. **Evidence identifies exact source.** A commit alone is sufficient only for a clean matching tree. Otherwise use a
    sealed base commit, content-addressed initiative patch, untracked-file manifest, and resultant source fingerprint.
16. **Invalidation follows impact, not card direction.** A changed Framework/Family owner, consumed path, profile,
    TestKit/Forge/schema, or fixture stales every consuming packet—including prior gold and sibling adapters—and those
    certifications rerun on one new checkpoint before dependent work passes.

Foundation production starts from the ratified shared contract. Existing connector code is evidence, not a reusable
architecture: repeated mechanics move into a shared seam only when meaning, ownership, lifetime, and failure behavior
are identical across providers.

## Scope

The epic covers:

- `Koan.Data.Abstractions`, `Koan.Data.Core`, and shared Data family substrates;
- all Entity-persistence connectors discovered under `src/Connectors/Data`;
- `Koan.Data.Vector`, `Koan.Data.SearchEngine`, and every discovered Vector connector; and
- the Data AdapterSurface, VectorAdapterSurface, provider fixtures, Forge, facts, docs, and product claims that
  communicate conformance.

It excludes Cache providers, AI model providers, and unrelated pillars unless a Data change requires a narrow
consumer compatibility update.

## Session protocol

1. Read `NOW.md`, `PROGRESS.md`, the selected card, `ACCEPTANCE.md`, and the primer sections named by the card.
2. Inspect `git status` and preserve every unrelated user change.
3. Verify prerequisites from source/tests. A green ledger entry is not proof.
4. Claim exactly one card in `PROGRESS.md`. At most one card is `in-progress` unless the orchestrator explicitly opens
   leased independent lanes after DAC-30. In lane mode, the orchestrator alone edits `PROGRESS.md` and `NOW.md`; a
   worker writes only its leased evidence directory and `handoff.md`.
5. For production-code cards, run the repository `explore` skill before editing. Record the owner and `keep`, `absorb`,
   `rebuild`, or `delete` disposition of existing scaffolding, plus the contract reason for every new moving part.
6. Before editing, expand the card's write boundary into exact allowed and forbidden paths. Execute only the card.
   Record unrelated defects in the Divergence log; do not fix them.
7. Run focused verification while working and the card's certification boundary before declaring PASS.
8. In serial mode, update the evidence packet, `PROGRESS.md`, and replace `NOW.md` with the exact next safe action. In
   leased lane mode, publish only the scoped packet/handoff and let the orchestrator merge central state.
9. Do not commit, push, publish, alter external data, or run destructive version-control commands unless the user
   explicitly authorizes that action.

## Evidence and secret handling

- Pin the exact reproducible source identity, primer/profile fingerprint, driver/provider version, fixture identity,
  source postures, and date. Before certification, the operator must provide either an authorized checkpoint commit or
  a sealed patch/source-manifest checkpoint that can be applied in a disposable clean worktree.
- Use least-privilege provider identities. Read-only and external-lifecycle cases require provider-enforced boundaries.
- Never commit connection strings, credentials, business values, raw sensitive provider errors, or high-cardinality
  tenant/source identifiers.
- Large logs, dumps, and benchmark traces belong in CI artifacts or ignored artifact storage. Commit stable summaries,
  hashes, commands, and safe native plans needed for reproduction.

## Write-boundary rules

- Audits and certifications may write only their initiative evidence/handoff and orchestrator-owned ledger files;
  production, product claims, shipped docs, and shared tests are read-only unless the card names a separate path.
- Gold harvest produces only provider facts, public-contract facts, black-box scenarios, negative lessons, and a
  retirement inventory. Gold replacement starts from an empty implementation root; it does not copy, port,
  mechanically transform, structurally preserve, or bridge the legacy implementation.
- Gold review and certification judge the new implementation through contracts, native evidence, black-box behavior,
  architecture, and complete retirement. Provenance ceremony is not a substitute for those proofs.
- Remediation cards list exact project/file roots and one semantic owner. An `allowed` label by itself grants nothing.
- Characterization tests belong to the audit packet unless a card explicitly permits a repository test path.
- Claim/product/docs/CI changes require their named card and human claim authority; they cannot hide a RED behavior.

## Stop conditions

Stop and report when:

- the primer is ambiguous in a way that changes public behavior;
- a prerequisite is not genuinely green;
- the required real provider cannot run;
- a remediation crosses a second semantic owner not named by the card;
- an adapter claim has no corresponding acceptance cell;
- an acceptance cell has no executable verifier; or
- the only apparent solution duplicates Framework behavior inside an Adapter;
- a gold replacement retains an old/new bridge, shadow registration, compatibility implementation, or fallback; or
- a proposed moving part has no necessary contract, repeated-mechanics, or measured hot-path reason.

A truthful RED, DEFER, or STOP is useful evidence. False green is an epic failure.
