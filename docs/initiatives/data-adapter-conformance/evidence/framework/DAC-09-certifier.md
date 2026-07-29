---
type: SPEC
domain: data
title: "DAC-09 Independent Framework Certifier Receipt"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: red
  scope: independent Framework gate, public-surface projection, structural protocol, compile examples, behavioral suites, strict Forge, and SQLite/Mongo seam readiness
---

# DAC-09 independent Framework certifier receipt

## Verdict

**RED.** DAC-09 must remain red and must not unlock DAC-10 or DAC-20. The certifier found two independently
reproducible Foundation-owned failures:

1. the required strict Forge path cannot execute the shared `G-09/row/Adapter` oracle because the TestKit registers a
   managed field after host composition; and
2. the sealed public `consumer-contract.cs` fixture does not compile and is not covered by the repository's code-example
   validator.

The many legacy adapter receipt failures listed below are real, but they are not used to manufacture this Framework
verdict. The two Foundation failures are sufficient on their own. Prior PASS wording and receipts were treated as
untrusted inputs.

No production source, ledger, NOW/progress file, work item, or other evidence file was changed by this certification.
Generated audit/compile products live only under ignored `artifacts/data-adapter-conformance/dac09-certifier/`.

## Candidate identity

The candidate was a dirty worktree, so `HEAD` alone is not its identity.

| Field | Sealed value |
|---|---|
| Branch | `agent/polymorphic-entity-root-persistence` (ahead of its upstream by 2) |
| `HEAD` | `86c18819cf03160c20a001d91f3bd2f257fd1a0d` |
| Tracked paths | 4,988 |
| Untracked, non-ignored paths | 517 |
| Manifest rows | 5,505 |
| Deleted tracked rows | 2 |
| Whole-worktree manifest SHA-256 | `cb9ca7bdf9fb7c5f39963482d5e4598a04d606fbcf886c106d2a7c82c5da6af3` |

The read-only manifest algorithm was:

```powershell
$tracked = @(git -c safe.directory=F:/Replica/NAS/Files/repo/github/sylin-org/koan-framework ls-files --cached)
$untracked = @(git -c safe.directory=F:/Replica/NAS/Files/repo/github/sylin-org/koan-framework ls-files --others --exclude-standard)
$paths = @($tracked + $untracked | Select-Object -Unique)
[Array]::Sort($paths, [StringComparer]::Ordinal)
# For each path, emit: path<TAB>lowercase-file-sha256<TAB>byte-length
# For an absent tracked path, emit: path<TAB>DELETED
# SHA-256 the UTF-8 encoding of the LF-joined rows, with no terminal LF.
```

During certification another independent agent published
`evidence/framework/DAC-09-adversarial-review.md`. A second read-only manifest contained 5,506 rows and hashed to
`5141a180946f92aff5cf1c55a099b19d32f351729abceb41c1a824990c7217d3`; excluding only that later evidence receipt
reproduced the sealed 5,505-row hash exactly. It did not change the source, tests, or tools under certification.

No DAC-09 initiative checkpoint was minted. A red candidate must not receive a green checkpoint.

## Dynamic Framework surface projection

Commands:

```powershell
pwsh -NoProfile -File docs/initiatives/data-adapter-conformance/tools/New-FrameworkSurfaceAudit.ps1 `
  -SourceRoot (Get-Location).Path `
  -OutputDirectory artifacts/data-adapter-conformance/dac09-certifier `
  -SourceCommit 'worktree:cb9ca7bdf9fb7c5f39963482d5e4598a04d606fbcf886c106d2a7c82c5da6af3' `
  -PrimerPath docs/architecture/data-adapter-development-primer.md

pwsh -NoProfile -File docs/initiatives/data-adapter-conformance/tools/New-FrameworkSurfaceMap.ps1 `
  -InventoryPath artifacts/data-adapter-conformance/dac09-certifier/public-api.json `
  -OutputDirectory artifacts/data-adapter-conformance/dac09-certifier
```

Results:

| Check | Result |
|---|---:|
| Projects inventoried | 27 |
| Public types | 638 |
| Public members | 2,509 |
| Total declarations | 3,147 |
| Parser failures | 0 |
| Surface-map rows | 52 |
| Declarations mapped exactly once | 3,147 |
| Unmapped / duplicate declaration assignments | 0 / 0 |
| Internal anchors | 10 |
| Alternate critic matches / unclassified | 417 / 0 |
| Primer vocabulary terms | 19 |
| Framework-owned surfaces | 27 |
| Framework A-H/P claim cells covered | 81 of 81, exactly once |

`public-api.json` hashed to `fd5beb5a60a451eadb26d4a4647b62ec8718039310fbefb02cad95be0b2fba6f` and
`surface-map.json` hashed to `cea704bb4c108383c6a177dc1830def5391c2086980ef3268e8e1adc1cc37163`.
This dynamic inventory intentionally supersedes DAC-01's smaller frozen count; the current expanded public surface is
fully classified.

## Structural, protocol, and build gates

Commands and results:

```powershell
dotnet test tests/Suites/Testing/Koan.Testing.Tests/Koan.Testing.Tests.csproj --no-restore --nologo --verbosity minimal
# PASS: 33 passed, 0 failed, 4 intentionally skipped.

pwsh -NoProfile -File docs/initiatives/data-adapter-conformance/tools/Test-Initiative.ps1
# PASS: cards=41, progress=41, roadmap=41, primerIds=105, packets=22, inProgress=0.

pwsh -NoProfile -File docs/initiatives/data-adapter-conformance/tools/Test-Initiative.Mutations.ps1
# PASS: 15/15 mutation sentinels.

pwsh -NoProfile -File scripts/forge-verify.ps1 -CatalogOnly `
  -CatalogOutput artifacts/data-adapter-conformance/dac09-certifier/data-conformance-catalog.json -Output json
# PASS: protocol data-adapter-conformance/1; 105 cells; 39 profiles.

git diff --check
# PASS (exit 0); only existing LF/CRLF warnings were emitted.

dotnet build Koan.sln --no-restore --nologo --verbosity minimal
# PASS: 0 warnings, 0 errors, 24.2 seconds.
```

The catalog primer fingerprint was
`5727df1efb1cf87385f5708f5c7dbf02b91b7fe8da4ce8af25ee4dc68c15befe`. It reported 28 concretely bound IDs and
77 generic/unbound IDs; all 105 are represented by the generic protocol projection, so CatalogOnly correctly passed.
That count is not represented here as adapter proof.

## Behavioral matrix

The focused Framework batch used the current solution-built binaries and this exact class filter:

```powershell
$classes = @(
  'SourcePolicySpec','SourceReadinessCoordinatorSpec','EntityExecutionSemanticsSpec',
  'QueryCoordinationReceiptSpec','SourceIntegrationSpec','DataDiagnosticsConformanceSpec',
  'EntityLifecycleSpec','DataAdapterParticipationSpec','AdapterResolveStorageSpec','StorageAnchorCacheSpec',
  'StorageNameParticleSpec','AodbComposeSpec','ManagedEqualityReadContributorSpec','ManagedFieldSeamSpec',
  'ReadScopeFailClosedSpec','StorageWritePlanSpec','WriteContributorSpec','CrossAdapterTransactionsSpec',
  'VectorNamePinWarningSpec','DirectDataAccessSpec','EntityStreamingSpec','TransactionBasicsSpec',
  'TransactionErrorHandlingSpec','TransactionStateValidationSpec','WithContextInheritanceSpec',
  'StartKoanHostOwnershipSpec','RelationshipPointwiseSpec','RelationshipMetadataHostOwnershipSpec'
)
$filter = ($classes | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
$env:Logging__EventLog__LogLevel__Default = 'None'
dotnet test tests/Suites/Data/Core/Koan.Tests.Data.Core/Koan.Tests.Data.Core.csproj `
  --no-build --no-restore --nologo --verbosity minimal --filter $filter
```

Results:

| Lane | Passed | Failed | Classification |
|---|---:|---:|---|
| Focused Framework/Data Core | 263 | 4 | Four legacy JSON receipt failures; see separation below |
| Convergence | 19 | 0 | PASS |
| Relational | 16 | 0 | PASS |
| Axes | 56 | 0 | PASS |
| SQLite relationship isolation | 4 | 1 | One legacy SQLite receipt failure |

The four Data Core failures use the fixture's default JSON adapter. One query omitted `FilterHandled=true`; three
mutation totals omitted the required `CountExecution`. The SQLite relationship failure likewise omitted
`FilterHandled=true`. The Framework's strict `QueryReceiptValidator` rejected each inaccurate receipt as designed.

## Strict Forge: Foundation RED

Command:

```powershell
pwsh -NoProfile -File scripts/forge-verify.ps1 -DockerFree -Strict -NoBuild -Output json -DeadlineSeconds 300
```

The command completed in 25.7 seconds with exit 1: 0 green adapters, 3 red record adapters, 0 deferred adapters, 2
vector infrastructure outcomes, and 0 protocol errors. Targeted non-strict reproductions were:

```powershell
pwsh -NoProfile -File scripts/forge-verify.ps1 -Adapter InMemory -Plane record -NoBuild -Output table
pwsh -NoProfile -File scripts/forge-verify.ps1 -Adapter Json     -Plane record -NoBuild -Output table
pwsh -NoProfile -File scripts/forge-verify.ps1 -Adapter Sqlite   -Plane record -NoBuild -Output table
```

InMemory and JSON each passed five cells and failed `G-09/row/Adapter`. The shared failure was:

```text
InvalidOperationException: Managed-field registration must be declared while Koan is composing the application.
Place the declaration inside builder.Services.AddKoan(() => { ... }) or a Koan module registration method.
```

This is a Foundation/TestKit integration defect, not an adapter receipt defect:

- `AodbConformanceSpecsBase.Shared_isolation_holds` first calls `BootAsync()`;
- it then invokes `ManagedFieldNoLeak.AssertNoLeakAsync()`;
- that helper calls `ManagedFieldRegistry.Reset()` and `ManagedFieldRegistry.Register(...)` after composition; and
- `ManagedFieldRegistry.Register` correctly requires an active `KoanCompositionScope`.

Thus the shared oracle contradicts the host-owned composition contract it is intended to certify. It prevents two
otherwise healthy adapter rows from reaching adapter behavior, so the mandatory strict gate cannot be green.

SQLite's additional record failures and missing strict packets are Adapter/receipt work. The two vector infrastructure
outcomes are registered but unsupplied vector proof seams outside DAC-09's A-H/P Framework scope. Neither category
changes the Foundation RED above.

## Public compile and documentation checks: Foundation RED

Commands:

```powershell
pwsh -NoProfile -File scripts/docs-lint.ps1 `
  -Roots docs/initiatives/data-adapter-conformance/evidence/framework/DAC-09-certifier.md `
  -EnforceFrontMatter -Output list
# PASS for this receipt after publication. A broader initiative lint is not green because pre-existing
# evidence/work-item front matter uses values outside the repository lint vocabulary.

pwsh -NoProfile -File scripts/validate-code-examples.ps1 `
  -Files docs/architecture/data-adapter-development-primer.md,docs/initiatives/data-adapter-conformance/evidence/framework/public-contract.md,src/Koan.Data.Core/README.md,src/Koan.Data.Abstractions/README.md,src/Koan.Data.Relational/README.md,src/Koan.Testing/README.md `
  -TempDir artifacts/data-adapter-conformance/dac09-certifier/code-validation
# Exit 0, but scope=0: no selected file contained an opted-in instructional block.
```

Because that validator exercised zero examples, the certifier copied the sealed fixture unchanged into an ignored
temporary `net10.0` console project, referenced `Koan.Core` and `Koan.Data.Core`, and built it:

```powershell
dotnet new console --framework net10.0 --name Dac09ConsumerContract `
  --output artifacts/data-adapter-conformance/dac09-certifier/consumer-contract-compile --force --no-restore
Copy-Item docs/initiatives/data-adapter-conformance/evidence/framework/consumer-contract.cs `
  artifacts/data-adapter-conformance/dac09-certifier/consumer-contract-compile/ConsumerContract.cs
dotnet add artifacts/data-adapter-conformance/dac09-certifier/consumer-contract-compile/Dac09ConsumerContract.csproj `
  reference src/Koan.Core/Koan.Core.csproj src/Koan.Data.Core/Koan.Data.Core.csproj
dotnet restore artifacts/data-adapter-conformance/dac09-certifier/consumer-contract-compile/Dac09ConsumerContract.csproj `
  --ignore-failed-sources --nologo --verbosity minimal
# The generated template Program.cs was excluded, leaving the sealed fixture unchanged.
dotnet build artifacts/data-adapter-conformance/dac09-certifier/consumer-contract-compile/Dac09ConsumerContract.csproj `
  --no-restore --nologo --verbosity minimal
```

The final build failed with exactly one compiler error:

```text
ConsumerContract.cs(39,40): error CS0246: The type or namespace name 'Entity<,>' could not be found
```

`Entity<,>` is declared in `Koan.Data.Core.Model`; the fixture imports `Koan.Data.Core` but not that namespace. The
fixture says it is a compile contract, yet repository search found no project that compiles it, and the validator's
zero-example result confirms the coverage gap. Equivalent public journeys in `SourceIntegrationSpec` and mapping
tests do compile and pass, but they do not make this sealed target fixture green.

## Adversarial ownership and seam review

The following scans returned no candidates in the Framework/Family/TestKit scope and no copies of Framework machinery
inside data connectors:

```powershell
rg -n "ContinueWith|GetAwaiter\(\)\.GetResult\(\)|\.Result\b|\.Wait\(|Task\.Wait|StartsWith\(\"select|IsSchemaRelatedFailure|WithDataReadiness|ConcurrentDictionary" `
  src/Koan.Data.Abstractions src/Koan.Data.Core src/Koan.Data.Relational src/Koan.Testing tests/Suites/Data/AdapterSurface

rg -n "DataSourcePlan|SourcePolicy|RecordSetMaterializer|RecordProjector|MappingPlanCompiler|DataSourceReadinessCoordinator|QueryReceiptValidator|DataClaimSet|OperationPlan" `
  src/Connectors/Data
```

Current compile-ready seams were also checked directly:

```powershell
dotnet build src/Connectors/Data/Sqlite/Koan.Data.Connector.Sqlite.csproj --no-restore --nologo --verbosity minimal
dotnet build src/Connectors/Data/Mongo/Koan.Data.Connector.Mongo.csproj --no-restore --nologo --verbosity minimal
```

Both connector projects compiled with 0 errors. SQLite consumes the Abstractions/Core/Relational seams and implements
the shared repository/factory contracts. Mongo consumes Abstractions/Core and the Document-family base/factory
contracts. The Framework provides compile-ready repository, query/raw-query, conditional-write, source-integration,
mapping, readiness, receipt, inspection, transaction, and diagnostics seams used by the current A-H/P projection.

An exhaustive assertion about **future ratified** SQLite/Mongo target contracts is not yet possible: both `claims.json`
files contain empty pending claim arrays and both `rewrite/replacement.json` files are pending with empty compile,
registration, moving-part, and execution-path manifests. That bounded confirmation belongs after DAC-10/DAC-20 harvest
and DAC-15 ratification. This is recorded as a follow-up, not used as the present RED cause.

## Failure ownership

| Finding | Owner | DAC-09 effect |
|---|---|---|
| Shared G-09 oracle registers after composition | Foundation / AdapterSurface TestKit | **RED blocker** |
| Sealed `consumer-contract.cs` missing `Koan.Data.Core.Model` and not compiled by validation | Foundation evidence/test harness | **RED blocker** |
| Four Data Core fixture failures caused by JSON receipt omissions | Adapter(JSON), legacy receipt remediation | Not a Framework failure |
| SQLite relationship and strict record failures | Adapter(SQLite), legacy receipt/family remediation | Not a Framework failure |
| Missing strict adapter packets | Individual adapter certification cards | Not a Framework failure |
| InMemory/SQLiteVec vector infrastructure outcomes | Vector provider proof/infrastructure cards | Outside DAC-09 A-H/P scope |
| Future SQLite/Mongo manifests are empty pending harvest/ratification | DAC-10, DAC-20, DAC-15 | Bounded follow-up |

## Required remediation and re-entry

1. Change the shared TestKit/host setup so the G-09 managed-field descriptor is declared inside the real
   `AddKoan(...)` composition path. Do not weaken `ManagedFieldRegistry`'s host/composition guard and do not put the
   workaround in adapters.
2. Prove `G-09/row/Adapter` reaches and passes adapter behavior for both InMemory and JSON, then rerun strict Forge.
3. Repair the sealed public compile fixture and wire it into an executable compile test or opt-in validation path so a
   future namespace/API regression is observable.
4. Rerun the dynamic inventory, structural/mutation gates, focused Framework/Family suites, solution build, docs and
   exact public example compilation, and strict Forge against one newly sealed dirty-workspace identity.
5. Only after every Foundation lane is green may DAC-09 mint its checkpoint and unlock the two provider harvest cards.

Until those steps are evidenced by a fresh independent certifier, the checkpoint is **withheld** and the disposition
is **RED**, not PASS and not DEFER.
