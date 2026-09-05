---
type: ANALYSIS
domain: framework
title: "AE-01a - Published foundation consumption evidence"
audience: [maintainers, architects, ai-agents]
status: current
last_updated: 2026-09-05
framework_version: v1.0.0
validation:
  date_last_tested: 2026-09-05
  status: verified
  scope: released Koan dependencies, fresh-cache package consumers, HTTP and MCP policy boundaries
---

# Shared foundation with published Koan packages

Both Shared Approvals applications now run with published Koan packages. The local Core
preparation script, generated build import, preparation gate, and direct Core override are
removed. The foundation's capability references bring the repaired Core transitively.

The [release](https://github.com/sylin-org/koan-framework/actions/runs/33979924764) certified
and published 99 packages after [PR #139](https://github.com/sylin-org/koan-framework/pull/139)
passed and `dev` was fast-forwarded to `main` at
`2b9bc6989f9bbd489c3d0c4af2a5e90fd76d2071`. Publication ran in CI.

| Dependency | Restored version |
|---|---|
| Sylin.Koan.App | 1.0.23 |
| Sylin.Koan.Data.Connector.Sqlite | 1.0.30 |
| Sylin.Koan.Mcp | 1.0.29 |
| Sylin.Koan.Core, through dependency floors | 1.0.34 |

## Reproduce

Use the [workspace commands](../../../../samples/applications/SharedApprovals/README.md)
to run either application directly. For the package update experiment, use .NET 10 SDK,
PowerShell 7, Git, and access to nuget.org:

```powershell
pwsh samples/applications/SharedApprovals/verify.ps1
```

No framework preparation step is needed. The verifier creates a fresh package cache and a local
feed containing only the demonstration foundation. All Koan packages come from nuget.org;
their restored metadata is checked for each of the six consumer builds. The foundation's
controlled 1.0.1 and 1.0.2 versions are local experiment packages, not public releases.

## Observed results

All **168 assertions passed** in run `75a53a82ba4f4eeaa11c832001be682c`.
The [machine receipt](AE-01a.receipt.json) records source hashes, full package graphs,
the published Core package hash and source, and every assertion. The sample source is revision
`3956b6aca3ebd9a556ee0ac3b89d0520c72db2f2`; generated lockfile line endings may vary by host.
The original [163-check local receipt](AE-01.md) remains intact.

Both consumers passed the original approval and business-action contracts under the USD 1,000
baseline, USD 500 update, and isolated rollback. HTTP upsert and MCP could not bypass approval
policy. Rejected writes remained unpersisted. Existing completed records survived the update,
and a USD 750 request approved before tightening could still complete its business action.
Consumer source and the original foundation package bytes stayed unchanged across policy versions.

The ordinary ProjectReference authoring solution also restored solely from nuget.org and
built with zero warnings and errors. The 35 focused Core manifest/activation tests passed before
release; the release's package-only application certification passed. This follow-up did not
repeat the earlier browser walkthrough; the verifier checked both served pages and shared assets.

One retained attempt, `72b82c822da449548e7f843e51b07402`, failed because the chosen nested
artifact root produced a 263-character dependency path that MSBuild could not copy. The file
existed. Repeating with the standard, shorter artifact root passed without code changes.
On deeply nested Windows checkouts, choose a shorter directory with `-ArtifactRoot`.

## What this closes

AE-01a closes the local Core prerequisite and establishes a published Koan dependency baseline.
A03 still needs its remaining capability scenes, recording, GIF, and existing launch checks.
Identity, tenant isolation, concurrency, generated Code Mode SDK behavior, independent adoption,
and productivity measurements retain the [original evidence's limits](AE-01.md).
