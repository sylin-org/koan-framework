---
type: SPEC
domain: data
title: "Data Adapter Conformance Work-Item Template"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: bounded static and dynamically generated work-item shape
---

# DAC-XX — Outcome, not activity

| Field | Value |
|---|---|
| Phase | foundation / gold / fleet / vector / closure |
| Kind | harvest / audit / decision / remediation / ground-up replacement / replacement review / certification |
| Depends on | DAC-… |
| Unlocks | DAC-… |
| Required primer profiles/IDs | exact profiles and stable IDs |
| Production writes | allowed / forbidden summary |
| Allowed paths | exact repository roots/files and initiative evidence/handoff paths |
| Forbidden paths | every other owner, product/public truth unless approved, and unrelated work |
| One semantic owner | Framework / Family(name) / Adapter(name) |

## Meaningful outcome

State the application decision that becomes faithful, fast, or explainable after this card.

## User contract

- **Application expression:** exact public example or operation.
- **Guarantee:** observable result.
- **Correction:** fail-closed outcome when unsupported or misconfigured.

## Evidence to read

List exact current source, tests, docs, provider references, and predecessor packet rows. Citations are starting points;
the session re-verifies them. For a greenfield replacement, list only the closed rewrite inputs; legacy source/history,
legacy-coupled tests, and the retirement inventory are evidence, never design authority.

## Preflight

1. Verify every prerequisite from the repository/provider.
2. Pin the reproducible source checkpoint and claim rows.
3. Re-derive relevant execution surfaces.
4. Record expected evidence kinds from the primer.
5. Expand and verify the exact path allowlist; `allowed` without paths grants no write authority.
6. Query packet dependencies for every changed owner/path/tool/profile/fixture and freeze all impacted consumers.

## Decisions

- **DECIDED:** invariant the session cannot redesign.
- **DEFAULT:** preferred choice; deviation requires recorded evidence and justification.
- **OPEN:** question that forces STOP/human review if it affects public semantics.

## In scope

- One coherent responsibility and its tests/evidence/docs.

## Out of scope

- Every adjacent responsibility and any unrelated cleanup.

## Required artifacts

- Exact packet files/rows, source/tests, facts/docs, and generated outputs.

## Execution

Numbered, evidence-first plan. For remediation: reproduce RED, change one owner, prove red-to-green, run affected
shared cells, and update receipts/claims. For audit/certification: do not change production.

For a ground-up replacement: empty the adapter implementation, derive the new design from the ratified contracts and
provider facts, drive implementation from black-box conformance, and atomically retire the former implementation.
Record every moving part and its contract or hot-path reason. Never copy, port, mechanically transform, preserve the
old internal structure, or introduce old/new compatibility paths.

For a gold correction: change only the new implementation against a failing black-box case, regenerate the complete
new-source and moving-part manifest, and require the provider's review card → DAC-23 → both certifications. A copied
architecture or incomplete retirement is not corrected with a shim; restart from an empty implementation.

For replacement review: verify the atomic change, retirement inventory, native behavior, architecture, and absence of
compatibility/shadow/dead paths. Emit black-box requirements, never transplant advice.

For an `audit-certification` card, the first invocation freezes findings and the second invocation uses a different
reviewer and clean fixture. Only the second may certify. A RED produces a dynamic remediation dependency; it is never
fixed during either read-only invocation.

## Verification

Exact commands, provider posture, evidence kinds, mutation/failing-before proof, lint, and review roles.

## Definition of done

- [ ] Every named scorecard row has the required evidence and correct verdict.
- [ ] Claims and declines reconcile with runtime facts and public truth.
- [ ] Focused and boundary verification pass.
- [ ] Packet, PROGRESS, and NOW are updated.

## Stop conditions

List ambiguity, ownership expansion, missing provider, or prerequisite failures that stop the card.

## Session close

Use the handoff block in `LAUNCH.md`. Do not commit or proceed to the next card.
