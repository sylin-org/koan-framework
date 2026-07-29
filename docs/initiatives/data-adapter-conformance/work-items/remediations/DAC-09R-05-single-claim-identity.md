---
type: SPEC
domain: data
title: "DAC-09R-05 Single claim identity"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: red
  scope: A-01 H-01 H-04 P-06 claim remediation
---

# DAC-09R-05 — Make one claim set select runtime and conformance behavior

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-09R-04 |
| Unlocks | DAC-09R-06 |
| Required primer profiles/IDs | A-01, H-01, H-04, P-06 |
| Production writes | Allowed only for Framework/TestKit claim identity |
| Allowed paths | `src/Koan.Data.Abstractions/Diagnostics/**`; `src/Koan.Data.Abstractions/IAdapterFactory.cs`; `src/Koan.Testing/Conformance/**`; `tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs`; Framework claim/facts/health tests; compile-only connector contract probes; card evidence/ledgers |
| Forbidden paths | SQLite/Mongo production implementations before their empty-root cards; source routing; diagnostics rendering; unrelated work |
| One semantic owner | Framework claim identity and applicability |

## Meaningful outcome

The capabilities an application sees are exactly the capabilities the shared TestKit selects and the evidence packet
must prove.

## User contract

- **Application expression:** `Data.Source("LegacyErp").Describe()`.
- **Complete intent surface:** reference/configure an adapter; adapter authors declare one inert `DataClaimSet`.
- **Guarantee:** runtime, startup, facts, health, TestKit, and packet projection consume identical deterministic claim
  references without constructing a repository/client.
- **Correction:** missing or conflicting claims remain unadvertised and their public operations fail closed.
- **Public concepts:** `DataClaimSet` is the sole claim authoring and consumption seam; `CapabilitySet` is not an
  independent applicability authority.

## Execution

Remove or mechanically derive parallel manifest/TestKit capability selection, add drift mutations, and prove the gold
connector projects can compile against the single seam without changing their legacy implementations.

## Verification

One declaration mutation changes every projection together; no runtime/TestKit path constructs a provider to discover
claims.

