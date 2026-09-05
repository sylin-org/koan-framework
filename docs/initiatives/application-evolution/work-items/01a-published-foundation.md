---
type: PLAN
domain: framework
title: "AE-01a - Published foundation consumption"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-09-05
framework_version: v1.0.0
validation:
  status: reviewed
  scope: release and consumer cleanup specification; published-consumer proof pending
---

# AE-01a — Remove the local Core prerequisite

The maintainer requested fixing the remaining local-package limitation after AE-01. Execution
status lives in [PROGRESS](../PROGRESS.md); preserve the original local receipt as historical evidence.

**Task:** Release the existing Core identity repair and return Shared Approvals to ordinary NuGet consumption.

**Application intent:** Teams consume their organization's foundation without building Koan locally.

**Public expression:** The foundation references published Koan capabilities; each consumer keeps
its existing Entity/controller/module and bare `AddKoan()`. `dotnet run` needs no preparation script.

**Guarantee/correction:** Both applications restore the repaired Core through published dependency
floors, boot, and preserve the policy/update/rollback contracts. Missing packages fail normal NuGet
restore; generated malformed identities still receive Core's existing corrective failure.

**Complete intent surface:** .NET 10, Git and PowerShell for the verifier, network access to nuget.org,
the existing references and application configuration. No local Core feed or generated Framework.props.

**Public concepts:** None added. This removes a temporary contributor-only setup concept.

**Docs read:** Contributor law and architecture principles assign ordinary identity and composition
to Core; engineering workbooks route release work; the NuGet release playbook owns computed versions,
dependent stamps, fast-forward promotion, and CI publication; AE-01 evidence records the actual bug
and existing 35 Core tests / 163 application assertions.

**Code read:** The committed manifest repair is already tested; `DependencyStamper` mechanically
advances dependency floors; release scripts and workflow plan/pack/prove before isolated publication;
sample build props/targets, project, preparation script, and verifier contain the temporary local
dependency and its provenance check.

**Reusing:** Existing Core repair, release tooling, private-foundation package experiment, and HTTP/MCP
contract checks. Searches found `KoanCoreVersion` and `frameworkReceipt` only in the sample's temporary
setup; no new option, DTO, registry, or composition mechanism is needed.

**Creating new:**

| Change | Location | Responsibility |
|---|---|---|
| Generated dependency floors | Existing `dependency-versions.json` files selected by the stamper | Carry the Core fix through published dependency ranges |
| Remove local import and preparation gate | `samples/applications/SharedApprovals/Directory.Build.props` and `.targets` | Ordinary sample builds |
| Published capability references | `samples/applications/SharedApprovals/Foundation/Example.Approvals.Foundation.csproj` | Consume versions actually released |
| Remove preparation script | `samples/applications/SharedApprovals/prepare-framework.ps1` | Delete the superseded local-only workflow |
| Verify published package provenance with isolated cache/feed | `samples/applications/SharedApprovals/verify.ps1` | Keep the same application contracts with stronger dependency evidence |
| Current setup and release receipt | Sample READMEs/AGENTS/TECHNICAL and initiative evidence/ledgers | Separate historical local proof from published first use |

**Coalescence:** Keep the manifest repair at Core and the normal release pipeline as publication owner.
Delete the sample's local Core preparation path. Do not add another package-name convention or
application-side workaround. Dependency stamps remain generated, never hand-set versions.

**Ergonomics:** People and agents need one fewer setup step. Existing business syntax and ownership
stay intact; package guidance remains matched to the controlled foundation-policy versions.

**Constraints satisfied:** No new endpoints, provider registration, host globals, or data access.
Use the existing focused proof and required PR/release checks. The user's request and standing
promotion authorization cover this repair; no additional approval pause is required.

**Risks:** NuGet existence and restore indexing may propagate separately. Record the actual release
and use a clean cache to prove availability. Keep the original local Core receipt intact. The
generated Code Mode SDK issue is separate and is not solved or claimed by this release.
