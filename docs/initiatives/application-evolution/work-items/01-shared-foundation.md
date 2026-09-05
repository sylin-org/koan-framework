---
type: PLAN
domain: framework
title: "AE-01 - Shared application foundation"
audience: [maintainers, architects, ai-agents]
status: current
last_updated: 2026-09-05
framework_version: v1.0.0
validation:
  status: reviewed
  scope: shared foundation implemented; linked local package, HTTP, MCP, and browser proof
---

# AE-01 — Shared application foundation

Read the [charter](../README.md); claim and report work in [PROGRESS](../PROGRESS.md).
Dependency: A03's runnable application baseline, coordinated during discovery. The recording
is not required. This card owns the foundation and second consumer; A03 owns the flagship.

Implementation: [Shared Approvals](../../../../samples/applications/SharedApprovals/README.md).
Results and constraints: [AE-01 evidence](../evidence/AE-01.md). The live ledger owns status.

## Outcome and existing evidence

One shared foundation supports an approval desk and a distinct expense-request application.
It carries capability choices, shared contracts, an executable approval policy, and guidance
for developers and agents. Each consumer retains its own business behavior and usable interface.

Start with [A03](../../announcement/work-items/A03-flagship-demo.md),
[solution compositions](../../../capabilities/solutions.md),
[access rules](../../../recipes/control-who-can-do-what.md), and
[module authoring law](../../../../CLAUDE.md). Inspect the current capability map and recipes
before selecting exact packages; a bundle reference alone does not enforce a business rule.

## Deliver and prove

1. Establish A03's canonical source path and baseline revision. Record shared versus
   application-owned responsibilities and the consumer contracts to preserve.
2. Compose a small shared foundation using existing module/policy seams. Keep ordinary Entity
   operations in their natural grammar. Put the chosen approval rule in one executable owner.
3. Build the expense-request consumer with a workflow observably different from the flagship.
   Include concise extension guidance and a first-use path for both people and agents.
4. Exercise versioned foundation consumption using the repository's packaging conventions;
   keep the demonstration feed local. Reference published Koan packages at recorded versions.
5. Change one shared policy, update both consumers, and check both permitted and rejected
   operations plus each application's preserved contracts. Restore the prior foundation in
   an isolated fixture and verify the earlier behavior without implying data rollback.

## Acceptance and limits

- Both applications run from documented commands and actually consume the same foundation.
- The version/update receipt identifies both package graphs and the single shared policy owner.
- The policy change affects both consumers; their distinct application behavior remains valid.
- Consumer code, configuration, guidance, and human intervention are counted as adoption work.
- Guidance and behavior proof are linked beside the implementation and from the ledger.

Redirect if apparent reuse requires copied enforcement, hidden provider assumptions, or a
second composition mechanism. Route a demonstrated framework gap to its owner; keep domain
policy in the application foundation. Marketplace distribution and a generic approval engine
are outside this first milestone.

## Exploration — 2026-09-05

**Task:** Build the shared approval foundation, purchase consumer, expense consumer, and a
reproducible package-update/rollback experiment.

**Application intent:** Two teams use one approval policy while owning purchasing and expense
workflows separately; a foundation update changes both teams' approval limit.

**Public expression:** `PurchaseRequest : ApprovalRequest<PurchaseRequest>` and
`ExpenseClaim : ApprovalRequest<ExpenseClaim>` retain Entity operations; each consumer declares
its controller and a business module inheriting `ApprovalPolicyModule<TEntity>`. Each host
calls bare `AddKoan()`. The foundation references published `Sylin.Koan.App` 1.0.22,
`Sylin.Koan.Data.Connector.Sqlite` 1.0.29, and `Sylin.Koan.Mcp` 1.0.28, verified on NuGet
2026-09-05. Consumers reference the foundation project during authoring and its local NuGet
package during the version experiment.

**Guarantee/correction:** One host-owned lifecycle policy rejects invalid or over-limit approvals,
direct insertion of an approved record, and edits to already-approved common fields. HTTP
translates a lifecycle rejection to a corrective 409; MCP reaches the same persistence boundary.
Purchase ordering and reimbursement add their own lifecycle rules. Existing approved records
remain readable when the limit tightens. This is a local business-policy demonstration;
authenticated identity and tenant isolation belong to AE-03 and are not asserted by AE-01.

**Complete intent surface:** .NET 10.0.302, the declared references, one Entity/controller/module
per consumer, SQLite connection intent, local MCP configuration, and loopback host URLs. Shared
UI assets are ordinary files. Package versions in the local experiment come from NBGV commit
height in an isolated fixture; no published package version or release inventory is changed.

**Public concepts:** An approval request owns common state; a typed policy owns the organization
limit; a module binds that policy to the consumer's Entity; the consumer owns ordering or
reimbursement. Each concept corresponds to a business decision or enforcement boundary.

**Docs read:**

- [Engineering workbooks](../../../engineering/README.md) locate sample and verification rules.
- [Architecture principles](../../../architecture/principles.md) assign policy to its semantic owner.
- [Documentation navigation](../../../toc.yml) establishes the public routing surface.
- [Project introduction](../../../../README.md) establishes the reference/Entity/controller grammar.
- [Sample organization](../../../engineering/samples-organization.md) requires real reuse and a
  proved application before curriculum graduation.
- [Entity lifecycle](../../../reference/data/entity-lifecycle.md) establishes host ownership,
  stable prior state, and persistence coverage across REST and MCP.
- [Access recipe](../../../recipes/control-who-can-do-what.md) separates business policy,
  authorization, and tenancy; this milestone does not substitute one for another.

**Code read:**

- [FirstUse Approval](../../../../samples/FirstUse/Domain/Approval.cs) shows the smallest governed model.
- [FirstUse project](../../../../samples/FirstUse/FirstUse.csproj) separates source and package consumers.
- [GardenCoop Reading](../../../../samples/journeys/GardenCoop/01-GardenJournal/Models/Reading.cs)
  demonstrates lifecycle declarations during module composition.
- [KoanModule](../../../../src/Koan.Core/KoanModule.cs) owns registration and evidence per host.
- [Lifecycle builder](../../../../src/Koan.Data.Core/Lifecycle/EntityLifecycleBuilder.cs) supplies
  the existing binding seam without service location or assembly scanning.
- [Koan web startup](../../../../src/Koan.Web/Hosting/KoanWebStartupFilter.cs) owns static files,
  middleware, and controller mapping; consumer Program files need none of that wiring.

**Reusing:** Already exists: Entity statics, lifecycle prior state/cancellation, module discovery,
controller projection, static files, SQLite, MCP, and runtime facts. Explicit constants/options/DTO
searches found only the small FirstUse-specific identifiers, not a reusable spending policy.

**Creating new:** All paths below are relative to `samples/applications/SharedApprovals/`.

| Files / types | Responsibility and placement |
|---|---|
| `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `SharedApprovals.slnx` | Portable package-consumer fixture, isolated from repository-only source/analyzer injection |
| `Foundation/Example.Approvals.Foundation.csproj`, `Foundation/version.json` | Demonstration package, NBGV version ownership, published capability references |
| `Foundation/Domain/ApprovalRequest.cs`, `Foundation/Domain/ApprovalState.cs` | Shared business contract and state |
| `Foundation/Policy/ApprovalPolicy.cs`, `Foundation/Policy/ApprovalPolicyOptions.cs` | One immutable approval policy, typed limit, lifecycle decisions |
| `Foundation/Infrastructure/ApprovalConstants.cs` | Shared route and corrective-code vocabulary |
| `Foundation/Initialization/ApprovalPolicyModule.cs` | Bind policy, HTTP correction, and evidence through existing module composition |
| `Foundation/Web/ApprovalController.cs`, `Foundation/Web/ApprovalExceptionFilter.cs`, `Foundation/Web/ApprovalPolicyController.cs` | Shared approval action, corrective HTTP response, and user-visible spending limit |
| `ApprovalDesk/ApprovalDesk.csproj`, `Program.cs`, `appsettings.json`, `Domain/PurchaseRequest.cs`, `Initialization/PurchaseModule.cs`, `Infrastructure/PurchaseConstants.cs`, `Web/PurchasesController.cs` | First consumer and its purchase-order extension; subpaths after the project are within ApprovalDesk |
| `ExpenseDesk/ExpenseDesk.csproj`, `Program.cs`, `appsettings.json`, `Domain/ExpenseClaim.cs`, `Initialization/ExpenseModule.cs`, `Infrastructure/ExpenseConstants.cs`, `Web/ExpensesController.cs` | Second consumer and its reimbursement extension; subpaths after the project are within ExpenseDesk |
| Each consumer's `wwwroot/index.html` and `start.bat`; `Web/app.js`, `Web/site.css` | Two usable business interfaces, shared presentation mechanics, standard launchers |
| Root `verify.ps1`, `README.md`, `AGENTS.md`; project `README.md` and foundation `TECHNICAL.md` | Executable package/update proof, extension guidance, limits, and agent entry point |

**Coalescence:** Closest pattern is GardenCoop's module-bound lifecycle. Keep the framework seam;
create application-specific shared approval policy after identifying both real consumers. The
foundation owns common fields and approval decisions; consumers own their distinct terminal
business actions. A framework-wide spending rule would be too broad, copied consumer enforcement
too narrow. No existing implementation is superseded or removed.

**Ergonomics:** Application code continues to name its business Entity and inherited approval
operation. Module inheritance expresses the deliberately shared policy binding once per consumer;
it avoids custom discovery, service location, and provider registration. Package upgrades change
the policy without editing consumer source. Config, UI, tests, and adoption steps remain visible.

**Constraints satisfied:** Controller-only HTTP; bare `AddKoan()`; first-class Entity operations;
bounded list reads; constants and typed policy values; host-owned lifecycle; one type per file;
reusable-project docs. No provider parity, production auth, tenant, or concurrency guarantee is
inferred from the local SQLite demonstration.

**Risks:** Verify transitive package/module/controller discovery in a clean package consumer,
corrective HTTP translation, inherited generic Entity mapping, and unchanged approved rows after
policy tightening. Probe real behavior before extending the fixture. User authorization covers
this implementation scope; the exploration workflow requires no additional approval here.

## Core manifest repair exploration — 2026-09-05

**Task:** Align runtime reference-manifest validation with the build writer's ordinary package identities.

**Application intent:** An organization can name its shared foundation normally and consume its package.

**Public expression:** The existing `Example.Approvals.Foundation` reference and bare `AddKoan()`;
no new application registration, identity attribute, or package-name prefix.

**Guarantee/correction:** Build-generated foundation edges survive startup parsing. Invalid record
shape, kind, empty identity, or unsupported identity characters still fail with corrective guidance.

**Complete intent surface:** Unchanged application references and code; this repair requires a Core
package containing the fix. The published Core 1.0.33 failure is retained in the local verifier logs.

**Public concepts:** None added. Ordinary package identity remains the existing composition identity.

**Docs read:** Contributor law and architecture principles assign ordinary identity/composition to
Core; Core README and TECHNICAL describe generated reference provenance; the release playbook requires
computed versions and local package proof before publication.

**Code read:** `KoanApplicationReferenceManifest` rejects non-Sylin canonical identities;
`Sylin.Koan.Core.targets` and `Sylin.Koan.SemanticActivation.targets` already write arbitrary valid
package identities; `SemanticActivationCompiler` follows those ordinary dependency edges;
`SemanticId` permits names beyond the framework prefix; existing parser and activation specs locate
the focused regression boundary.

**Reusing:** The schema, records, build writer, dependency traversal, and corrective error already
exist. Searches found no shared package-identity validator; semantic IDs have a broader grammar
(including colon) and cannot substitute for the build writer's package grammar.

**Creating new:**

| Code | Location | Reason |
|---|---|---|
| Replace prefix checks with writer-compatible identity validation | `src/Koan.Core/Composition/KoanApplicationReferenceManifest.cs` | Runtime owner of the manifest contract |
| Ordinary package/project and malformed-name cases | `tests/Suites/Core/Koan.Core.Tests/Composition/KoanApplicationReferenceManifestSpec.cs` | Parser regression coverage |
| Foundation dependency activation with unrelated module inactive | `tests/Suites/Core/Koan.Core.Tests/Semantics/SemanticActivationCompilerSpec.cs` | Preserve reachability semantics |
| Ordinary foundation identity guidance | `src/Koan.Core/TECHNICAL.md` | Existing composition documentation owner |

**Coalescence:** Closest pattern is the build writer's `validIdentity`. Keep the writer and runtime
parser as their existing build/runtime boundaries; delete the runtime-only Sylin-prefix restriction.
Core is the one owner; application renaming would hide the bug and pillar changes would misplace it.

**Ergonomics:** No extra concept or IntelliSense surface. People and agents use the organization's
package name unchanged. A real local-package consumer provides the host-level proof.

**Constraints satisfied:** No new endpoints, provider wiring, options, or data access. Existing
schema and async surfaces remain intact; documentation follows the owning Core project.

**Risks:** Published packages cannot include an unmerged fix. Record local patched-Core provenance
explicitly and do not call that a nuget.org-only success. Existing source/package graph tests plus
the two real hosts must pass before treating this gap as repaired.

**Local proof preparation:** `samples/applications/SharedApprovals/prepare-framework.ps1` will pack
the repaired Core with its computed version into a local feed and write ignored `.local/Framework.props`.
The existing workspace props import that file and the foundation takes an explicit Core reference.
The verifier retains that Core package hash/version and uses a fresh cache. This is temporary,
visible contributor preparation until the fix is published; it does not publish or alter versions.
