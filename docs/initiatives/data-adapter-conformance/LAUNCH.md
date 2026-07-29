---
type: GUIDE
domain: data
title: "Data Adapter Conformance Session Launcher"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: paste-ready one-card launcher and handoff contract
---

# Data Adapter Conformance Session Launcher

Replace the card, file, mode, and lease placeholders, then paste the block into a fresh session. One card per session.

```text
You are executing one work item from the Koan Data Adapter Conformance Initiative. You have no prior session context;
the repository artifacts below are the complete handoff.

CARD: {{CARD_ID}}
FILE: {{CARD_FILE}}
MODE: {{SERIAL_OR_LEASED}}
LEASE: {{LEASE_ID_OR_NONE}}

Work in this order:

1. Read in full:
   - docs/initiatives/data-adapter-conformance/CHARTER.md
   - docs/initiatives/data-adapter-conformance/NOW.md
   - docs/initiatives/data-adapter-conformance/PROGRESS.md
   - {{CARD_FILE}}
   - docs/initiatives/data-adapter-conformance/ACCEPTANCE.md
   - the sections of docs/architecture/data-adapter-development-primer.md named by the card.
2. Inspect git status. Preserve all unrelated user changes. Do not reset, revert, commit, push, or publish. Verify the
   card's sealed source checkpoint can reproduce the exact initiative-owned tree; a commit is not sufficient when dirty.
3. Verify every prerequisite against source/tests/provider state rather than trusting the ledger. If one is false, STOP
   and report it; do not improvise around it.
4. Post a concise preflight: prerequisites and proof, pinned identity, exact allowed/forbidden paths and owners, plan,
   and any divergence. In serial mode, mark only this card in-progress in PROGRESS.md. In orchestrator-leased lane mode,
   verify the recorded lease and do not edit PROGRESS.md or NOW.md.
5. If production code is in scope, use the repository's explore skill before editing. Map business intent, layers,
   contracts/constants, closest evidence, owner, failure boundary, focused proof, and the `keep`, `absorb`, `rebuild`,
   or `delete` disposition. Justify every new moving part by a necessary contract or measured hot-path benefit.
6. Execute only the card. The primer is the semantic authority. Do not edit it to excuse a failure. Do not close a
   Framework RED in an adapter. Do not treat skipped LIVE evidence as green.
7. Before remediation, replacement, or gold correction, query packet dependencies for every changed
   owner/path/tool/profile/fixture and mark all
   consuming verdicts stale, including prior or sibling cards. Then verify empirically with every command/evidence kind
   in the card. Run mutation/failing-before proof for changed
   behavior and preserve provider-native plans, dispatch counts, fault artifacts, and provider-relative baselines.
8. Apply ACCEPTANCE.md independently. In serial mode, update the packet, PROGRESS.md, divergence/operator gates, and
   replace NOW.md with the exact next safe action. In lane mode, write only the leased packet and its `handoff.md`; the
   orchestrator validates and merges central state. Do not proceed to another card.

Hard rules:
- Full adherence means every claim passes and every decline fails closed; it does not mean provider feature parity.
- Audit cards do not change production. Certification cards do not fix failures.
- Existing code, tests, docs, supported status, and prior promotion packets are evidence—not authority.
- For SQLite and MongoDB gold work, legacy implementation is harvest evidence only. Replacement authors start from
  empty adapter implementations and may not copy, port, mechanically transform, structurally preserve, or bridge it.
- Shared Framework/Family contracts and revalidated provider SDKs are valid dependencies; legacy adapter source,
  helpers, control flow, fixtures, and compatibility machinery are not.
- Existing scaffolding has no compatibility entitlement. Delete duplicate ownership, speculative extension points,
  warm-path discovery, dead paths, and compatibility branches unless the ratified public contract requires them.
- Use a real pinned provider for LIVE. Missing infrastructure yields DEFER/BLOCK, never PASS.
- One semantic owner per change. Record out-of-scope findings; do not drive-by fix them.
- Auditors cannot narrow Observed/Advertised truth. Human product approval is required for Targets, withdrawals,
  downgrades, and non-shipping dispositions.
- Keep secrets, business data, raw sensitive provider errors, and high-cardinality identifiers out of artifacts.

End with:

## HANDOFF — {{CARD_ID}}
- Result: PASS | BLOCK | STOP
- Pinned identity: commit or sealed base+patch+source fingerprint, primer/profile fingerprint, provider/driver/fixture,
  source postures
- Packet: path and scorecard PASS/RED/DEFER counts
- Scope changed: files and semantic owners
- Claims/declines: exact changes and supporting acceptance IDs
- Impact invalidation: changed dependencies, stale packets, and required rerun cards
- Gold replacement proof: frozen rewrite inputs, empty-root proof, deleted legacy inventory, new-source manifest,
  architecture review, and absence result
- Verification: exact commands and decisive result lines
- Mutation/failing-before proof: evidence, or why not applicable
- Review findings: addressed, deferred with owner, or none
- Divergence/operator gates: entries added, or none
- Coordination: serial central-state update, or leased evidence/handoff path and orchestrator lease
- Next safe action: one exact card or human decision
```

The orchestrator re-runs the decisive checks and assigns the verdict. A session self-report never auto-promotes the
next row.
