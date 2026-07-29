---
type: REFERENCE
domain: data
title: "Koan.Data Framework Audit Identity"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: sealed DAC-01 public-surface audit identity
---
# Koan.Data framework audit identity

| Identity | Value |
|---|---|
| Card | DAC-01 |
| Audit date | 2026-07-27 |
| Base commit | `86c18819cf03160c20a001d91f3bd2f257fd1a0d` |
| DAC-00 source fingerprint | `e35b9f8eb9b6e4ea6f49e1bdcb29710ab18cd6af026236f0135db2a4e94820df` |
| Primer SHA-256 | `68a65d762d3facc0561eb92107020c4b21f7800bf0ea46f01b09c8ef20650382` |
| DATA-0110 SHA-256 | `0cd21d6ecfe338a296e4382550b579519bcfd9e8a98e9dd84ecea671b08cc044` |
| Source inventory SHA-256 | `e4df2a3ff247cbf71bf76a6f05c7b8cdd468d2cf3a61ed4f658e9933a8b4cd0d` |
| Surface map SHA-256 | `07359de1f4ae6017b7eaede2acf5a5c86560defbfb1f920e4bc26c158523ac1f` |
| Scorecard SHA-256 | `5135cfb5a6eac7da8d7dadec36d9cb55dc4fcca5b8f7c6fdaaa26fa7f448cf82` |
| Missed-path critic SHA-256 | `5998f4fbefb854ae1c1ffe83d2a734d0d17bec10b12281b8bd1c61c6c4ddeefb` |
| Audit identity | `codex:/root`, `framework-surface-auditor` |
| Production writes | none |
| Provider/driver/fixture | none; this is a static Framework audit |

The production source was read from the clean commit export at
`artifacts/data-adapter-conformance/audits/dac01-source-86c18819`. The live worktree's unrelated relational edits were
excluded.

Reproduce the packet with `New-FrameworkSurfaceAudit.ps1`, `New-FrameworkSurfaceMap.ps1`, and
`New-FrameworkScorecard.ps1`. These parse source/evidence only; they do not restore, compile, load a provider, or
change production source.
