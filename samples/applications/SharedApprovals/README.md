# One foundation. Two working desks.

ApprovalDesk turns a supplier request into a recorded purchase order. ExpenseDesk turns an
employee receipt into a reimbursement record. Both consume one approval foundation and keep
their own workflows, fields, controllers, and SQLite files.

## Run the applications

Use the .NET 10 SDK and PowerShell 7 for verification. Package restore needs nuget.org; the
applications themselves need no container, model service, credential, or remote database.

Prepare the local Core repair once from this repository:

```powershell
pwsh samples/applications/SharedApprovals/prepare-framework.ps1
```

The experiment found that published Core 1.0.33 rejects ordinary foundation package names at
startup. The repair is in this branch. Preparation packs Core at its computed version into a
local feed and records the package hash; it publishes nothing. Until that fix is released,
this is a contributor demonstration requiring this checkout, not a nuget.org-only example.

```powershell
dotnet run --project samples/applications/SharedApprovals/ApprovalDesk -- --urls http://127.0.0.1:5101
```

In another terminal:

```powershell
dotnet run --project samples/applications/SharedApprovals/ExpenseDesk -- --urls http://127.0.0.1:5102
```

Open each loopback URL. Submit a request, approve it, then place the order or record reimbursement.
The displayed approval limit comes from the shared foundation. A request above that limit stays
pending; the application supplies a corrective reason if a caller tries to approve it.

## Read the ownership

| Owner | Business meaning |
|---|---|
| [Foundation](Foundation/README.md) | Common approval fields, spending limit, final approved details, and matching package guidance |
| [ApprovalDesk](ApprovalDesk/README.md) | Supplier, cost center, and order reference |
| [ExpenseDesk](ExpenseDesk/README.md) | Employee, receipt, and reimbursement record |

Each host calls `AddKoan()` once. A business Entity inherits the shared contract, its controller
inherits the approval operation, and its module binds shared lifecycle policy before adding its
own rules. Consumer code never repeats the approval limit or provider registration.

The foundation references published Koan App 1.0.22, SQLite connector 1.0.29, and MCP 1.0.28,
plus the locally repaired Core recorded in `.local/framework.json`.
Workspace build files isolate these ordinary package consumers from repository-only build inputs.
Normal authoring uses a ProjectReference to the foundation; the verifier switches both consumers
to its actual NuGet package, leaving their business source unchanged.

## Verify a foundation update

```powershell
pwsh samples/applications/SharedApprovals/verify.ps1
```

The verifier creates an isolated copy and Git history, builds a USD 1,000 policy package, tightens
its single default to USD 500, and computes both versions from NBGV commit height. It uses a local
feed, checks both consumer graphs, and exercises allowed/rejected approvals and each consumer's
own business action. It retains logs, package hashes, runtime facts, and a machine-readable receipt.
It also checks approvals through MCP at and above the limit, denied-write persistence, and the
policy description shipped in each package. The controlled fixture updates that description
with the rule. This is simulated package history, not a claim about two public releases.
Rollback uses the earlier package against isolated data; it makes no claim to reverse business
decisions. No package is published and the authoring checkout is not rewritten by the experiment.

Read `AGENTS.md` for extension ownership and inspect the receipt's actual results before citing proof.
The [milestone evidence](../../../docs/initiatives/application-evolution/evidence/AE-01.md)
records the completed local run and its limits.

Shared browser assets are authored under `Web/`; the build copies them into each consumer's
ignored `wwwroot/app.js` and `wwwroot/site.css`. Edit the originals. Runtime MCP SDK output and
local databases are also ignored. Each application includes its generated composition lockfile.

## Operating boundary

This is a local demonstration of business policy. Anonymous local HTTP writes are intentional;
anonymous HTTP removal is unavailable under the declared access rule.
authenticated identity and tenant isolation have not been added. Bind only to loopback for this
exercise. HTTP MCP is development-only under Koan's default posture; production requires its
documented authentication configuration. These apps record orders and reimbursements without
contacting suppliers or moving funds.

The rule governs Koan persistence operations, not direct external SQL. Concurrency, external-system
delivery, and production hardening have no guarantee from this fixture. SQLite files persist across
restarts; upgrading or rolling back a package does not migrate or undo their records.

The application-evolution initiative owns the shared-foundation experiment; A03 owns ApprovalDesk's
flagship baseline and eventual recording. Independent user adoption and productivity remain
unmeasured until their evaluation work is executed.
