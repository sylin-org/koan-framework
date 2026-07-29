---
type: SPEC
domain: data
title: "DAC-02 Ratify the Data Adapter Contract and Public API"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: human decision prompt and ratification gate
---

# DAC-02 — Ratify the Data adapter contract and public API

| Field | Value |
|---|---|
| Phase / kind | foundation / human decision |
| Depends on | DAC-01 |
| Unlocks | DAC-14 |
| Primer scope | §§1–4 and every DAC-01 ambiguity |
| Production writes | no code; only the primer, approved DATA decision records, compile-contract fixtures, and initiative evidence |
| Owner | Data public contract |

## Meaningful outcome

An implementer can read the primer and know the exact compact application/API contract without interpreting
illustrative syntax, repeating fluent context, importing provider-family vocabulary, or adapting the specification to
current code.

## Required work

1. Present DAC-01's finite decision list grouped by user journey: source selection/policy, inspection, neutral records,
   registered operations, mapping, Entity persistence, diagnostics, and claims.
2. For each item, show the smallest user-delight example, exact public types/methods/config keys, observable guarantee,
   fail-closed correction, ownership, and concept cost.
3. Resolve especially:
   - typed `Data.Source(...)` composition/runtime handles;
   - `StorageLifecycle`, `Access`, and `ReadLanes` configuration/precedence;
   - opaque container/address types and inspection pagination;
   - the closed neutral value algebra and RecordSet accounting version;
   - compact `Query`/`Scalar` declaration, `Lane`, and provider binding verbs such as `Sql`, `Pipeline`, `Template`,
     and `Function`;
   - the `Container`/`Key`/`Property`/`Name`/`Path`/`Object` mapping grammar, composite/generated identity, codecs,
     and asymmetric bindings;
   - claim-manifest projection and Direct/provider-native boundaries; and
   - the Vector governance boundary: exact primer Source Core is shared, while similarity-specific semantics/IDs are
     deferred to the separately human-approved DAC-49 primer annex before any Vector TestKit implementation.
4. Update the primer only with human-approved outcomes. Create or update the appropriate DATA decision record so the
   API rationale is not re-litigated by implementation cards.
5. Make every target example compile as a consumer contract test or explicitly label it non-C# notation. The compile
   test may remain red only until DAC-04–DAC-07, but its expected API must be fixed.
6. Change the primer from proposed/design-only only when the human explicitly ratifies the whole contract.

## Decisions

- **DECIDED:** user-delight semantics and stable acceptance IDs govern; current code does not win conflicts.
- **DECIDED:** Koan does not become a relationship/unit-of-work ORM.
- **DECIDED:** full adherence remains claim-relative.
- **DECIDED:** fluent context is not repeated. The ratified baseline uses `Query`, `Scalar`, `Lane`, `Template`,
  `Container`, `Key`, `Property`, `Name`, `Path`, and `Object`; safety/capability qualifiers remain explicit.
- **DECIDED:** common literal container segments do not require a `StorageAddress` wrapper; the descriptor overload
  remains for programmatic composition and inspection.
- **OPEN:** any remaining exact ergonomic/API choice enumerated by DAC-01 that materially changes application action,
  safety, or future adapter scope requires human approval.

## Verification

- Run docs/link lint and code-example/consumer compile checks appropriate to the ratified examples.
- Search the primer for unresolved `proposed`, `illustrative`, `later`, `future`, or “ergonomic decision” language.
- Confirm every DAC-01 OPEN row resolves to a decision ID or remains an explicit operator gate blocking DAC-03.

## Definition of done

- [x] Human approval is recorded for every public semantic/API decision.
- [x] The primer is internally complete, uses the compact vocabulary consistently, and target examples have a compile guard.
- [x] The Vector annex process and single-catalog rule are approved; no Vector semantics are implemented by this card.
- [x] No implementation code changed and DAC-14 has an exact contract boundary for its quarantined workflows.

## Stop conditions

Stop for the human whenever two shapes materially change application ergonomics, security, or future adapter scope.
Do not select by majority-agent preference.
