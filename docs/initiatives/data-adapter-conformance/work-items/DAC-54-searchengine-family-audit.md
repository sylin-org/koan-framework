---
type: ARCHITECTURE
domain: data
title: "DAC-54 Audit and Independently Certify the SearchEngine Family Seam"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: shared Elasticsearch and OpenSearch family audit-certification prompt
---

# DAC-54 — Audit and independently certify the SearchEngine family seam

| Field | Value |
|---|---|
| Phase / kind | vector / family audit-certification |
| Depends on | DAC-30 |
| Primer scope | shared Source Core/Vector rows selected by the family manifest |
| Production writes | forbidden; family evidence/initiative handoff only |
| Owner | Family(SearchEngine) |

## Meaningful outcome

The shared SearchEngine seam has a frozen, independently verified responsibility boundary before Elasticsearch or
OpenSearch can inherit it. Their current Koan role is Vector, not an implied Entity/search ORM surface.

## Execute

1. Follow the two-invocation protocol in `ACCEPTANCE.md`: the first reviewer freezes the audit; after every dynamic
   remediation passes, a different reviewer reruns this card and is the only reviewer who may certify it.
2. Re-derive the two adapters and shared package boundaries. Freeze common and provider-delta scorecard rows before
   drawing an ownership conclusion.
3. Audit client ownership, source/collection naming, index lifecycle, mappings/dimensions, request construction,
   similarity query/filter translation, result/score mapping, paging, refresh/visibility, health, and failures.
4. Assign each row to Framework, Family(SearchEngine), Adapter(Elasticsearch), or Adapter(OpenSearch). Provider-name
   switches and lowest-common-denominator semantics are RED, not evidence of sharing.
5. RED family rows create one-owner dynamic remediation cards limited to `src/Koan.Data.SearchEngine/**` and shared
   family tests. This audit does not make those changes.
6. On the independent rerun, execute structural family tests and native request spies with mutations proving both
   adapters consume the family seam. These do not substitute for DAC-55/DAC-56 real-provider evidence.
7. Freeze the green family packet and exact provider-delta rows; keep dialect/version/auth behavior in adapters.

## Verification

- Focused builds/tests cover the family and both adapter assemblies.
- Mutation/removal of the shared translator, policy gate, or receipt mapping makes both dialect fixtures red.
- No production file or public provider claim changes in either invocation.

## Definition of done

- [ ] Each shared behavior has exactly one family owner and each delta exactly one adapter owner.
- [ ] Family tests prove structure and native requests without claiming LIVE conformance.
- [ ] Elasticsearch/OpenSearch public scope is accurately described as Vector unless separately specified.
- [ ] A different reviewer independently certifies the family packet after all remediation.
- [ ] Provider certification remains pending for DAC-55 and DAC-56.

## Stop conditions

Stop if production changes occur in this card, a public semantic needs amendment, a provider switch remains in the
family seam, the remediation implementer acts as certifier, or provider LIVE proof is mistaken for family structure.
