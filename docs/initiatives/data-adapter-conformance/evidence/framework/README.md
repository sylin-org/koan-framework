---
type: REFERENCE
domain: data
title: "Koan.Data Framework Conformance Packet"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: completed DAC-01 current-public-surface audit
---
# Koan.Data framework audit packet

DAC-01 passes as a complete RED audit of the current surface. It does not certify the current framework.

- 27 Data/family/extension/adapter projects were parsed from the frozen source export without restore or build.
- 479 public types and 1,832 public members—2,311 declarations total—resolve exactly once to 52 `SUR-*` surfaces.
- Ten internal chokepoints cover alternate execution paths that public declarations alone cannot expose.
- The missed-path critic matched 377 Direct/instruction/transaction/background/initialization/provider/patch/raw/
  connection/readiness/provisioning declarations with zero unclassified results.
- All 81 primer IDs are dispositioned: 39 Observed and 42 Target; current evidence is 71 RED and 10 DEFER.
- Ten reproducible findings resolve to 18 finite public-contract decisions for DAC-02.
- Exact declaration search confirms that the compact Source Integration, neutral result, mapping-plan, and registered-
  operation contracts do not currently exist.

Start with `surfaces.md`, `findings.json`, `scorecard.json`, and `decisions.json`. `public-api.json` and
`surface-map.json` are the mechanical authority; `evidence.json` resolves the complete packet.
