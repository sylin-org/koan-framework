---
type: REFERENCE
domain: data
title: "Qdrant Provider Probes"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: behavior-green
  scope: pinned Qdrant provider probes
---
# Qdrant probes

| Probe | Concern | Version | Least privilege | Command / fixture | Observation | Artifact | Official source |
|---|---|---|---|---|---|---|---|
| Live ledger | complete Vector/Source semantics | 1.18.3 | local test container | Qdrant VectorAdapterSurface project | 28 passed, zero skipped | test output and executable spec | [Qdrant API](https://api.qdrant.tech/api-reference) |
| Shape | dimensions, metric, named vector | 1.18.3 | collection read/create | V-01 wrong-shape and managed create | exact mismatch rejected | V-01 | [Collections](https://qdrant.tech/documentation/concepts/collections/) |
| Query | current endpoint and score direction | 1.18.3 | point read | V-07–V-10 | Query API works; three metrics normalize monotonically | V-08 | [Query points](https://api.qdrant.tech/api-reference/search/query-points) |
| Visibility | awaited mutation barrier | 1.18.3 | point write/delete | V-11 | immediate read/delete visibility | V-11 | [Points](https://qdrant.tech/documentation/concepts/points/) |
| Filters | declared operator convergence | 1.18.3 | payload read | V-13 corpus vs neutral oracle | all declared operators converge; StartsWith declines | V-13 | [Filtering](https://qdrant.tech/documentation/concepts/filtering/) |
| Durability | provider restart | 1.18.3 | container stop/start | V-23 stable endpoint fixture | stored point survives restart | V-23 | [Storage](https://qdrant.tech/documentation/concepts/storage/) |
| Strict Forge | repository certification boundary | protocol v1 | local Docker | `forge-verify.ps1 ... -Strict` | every row passed; overall deferred for missing packet | Forge console result | repository control plane |
