---
type: SPEC
domain: framework
title: "R08-04 - Compile Package-First Templates"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: exact 108-package candidate, generated templates, and package-only FirstUse/GoldenJourney proof
---

# R08-04 — Compile package-first templates

- Tranche: `T7B — V1 release readiness / package-first creation`
- Status: `passed`
- Depends on: passed R08-03
- Unlocks: first coherent public observation, then a real public-to-candidate upgrade/rollback proof
- Owner: release compiler owns shipped template package identities; templates own only business-readable source

## Meaningful outcome

A new developer runs:

```bash
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o MyApp
dotnet run --project MyApp
```

The generated project restores a coherent Koan package family and reaches its meaningful Entity API without
editing versions, adding configuration, understanding the repository, or choosing framework internals.

## Focused exploration

**Task:** remove hand-maintained package versions from Koan templates and make the release compiler ship a
coherent package-first creation contract.

**Application intent:** “Create a Koan application and run meaningful business code immediately.”

**Public expression:** install the latest stable template package, instantiate `koan-web` or `koan-console`, and
run it. No template parameter, package selection, version selection, configuration, context, or external backend
is required; SQLite is the template's zero-administration durable store.

**Guarantee/correction:** generated PackageReferences carry closed-open Koan compatibility bands derived from the
actual release/package graph. A missing selected/public floor, unknown token, incoherent band, wrong package ID,
or template output that cannot restore/build/run fails packing before publication with the package and correction.

**Complete intent surface:** the three commands above are complete. A developer may deliberately edit package
ranges later, but Koan does not ask them to repair generated identities.

**Public concepts:** none beyond standard `dotnet new` and NuGet PackageReference. Compatibility bands are emitted
project facts, not a Koan template option.

### Docs read

- Microsoft `dotnet new install` documentation — latest stable is the default and `@version` is the supported
  explicit-version form: <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-install>.
- Microsoft template-package guidance — a template pack is an ordinary NuGet content package installable from
  a feed or nupkg: <https://learn.microsoft.com/en-us/dotnet/core/tutorials/cli-templates-create-template-package>.
- Microsoft NuGet pack targets — `SuppressDependenciesWhenPacking=true` is the standard way to keep build-time
  project edges out of a content package's dependency payload:
  <https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets>.
- `docs/engineering/packaging.md` and ARCH-0085 — every package owns intent and internal compatibility uses one
  closed-open SemVer band.
- R08 parent and R08-03 — package-first proof follows one evidence-derived product surface and precedes remote
  publication.

### Code read

- both template project/config/source trees — currently small and business-readable; keep their application code.
- `templates/Sylin.Koan.Templates.csproj` — correctly uses standard `PackageType=Template`, but has no dependency/
  impact edges to the packages its generated projects consume.
- generated web/console csprojs — hard-code `0.17.*`; console is already incompatible with the current `0.18`
  foundation bundle.
- `PackagePipeline` — already owns exact selected/public package floors, compatibility validation, packing, and
  clean-room applications; absorb template materialization here rather than add a script.
- `PackageGraph` / release lineage — already owns project edges and breaking reverse closure; reuse it to mint the
  template when a referenced compatibility line changes.

## Coalescence decision

- **Closest pattern:** package dependency range compilation in `build/compat-ranges.targets` plus clean-room
  application preparation in `PackagePipeline`.
- **Specificity:** compatibility-band law is release tooling; template source meaning is template-local.
- **Keep:** standard `PackageType=Template`, two tiny application sources, one release manifest, one package graph.
- **Absorb:** C# compatibility parsing/formatting into one `PackageCompatibility` helper used by validation and
  template materialization; template packing/proof into `PackagePipeline`.
- **Rebuild:** hard-coded template versions as compiler tokens resolved from selected manifest floors or the latest
  compatible public package.
- **Delete:** floating `0.17.*` literals, manual template version bump instructions, and any claim that a local
  staged rehearsal is a public upgrade.
- **Target owner:** packaging owns the transformation because only it knows the exact candidate/public closure.
  Template JSON is too narrow and would duplicate release facts; runtime Core is too wide and too late.

## Exact placement

| New code | Location | Why here |
|---|---|---|
| compatibility value object | `tools/Koan.Packaging/Models/PackageCompatibility.cs` | one parser/formatter for existing validation and template emission |
| template materializer | `tools/Koan.Packaging/Services/TemplatePackageCompiler.cs` | one focused owner turns tokens plus release facts into staged content |
| template package IDs/tokens | `PackagingConstants.TemplatePackage` | stable release vocabulary, not string literals |
| graph-only project edges | `templates/Sylin.Koan.Templates.csproj` | standard ProjectReferences express impact; suppressed package dependencies keep install clean |
| source tokens | generated template csprojs | sources state package intent without stale version judgment |
| pack integration | `PackagePipeline` | existing release/package chokepoint resolves floors and validates nupkg bytes |
| template package proof | `TemplatePackageProbe` | one focused owner installs the exact nupkg, creates both projects, and proves their business result in an isolated CLI home |
| clean-room application compiler | `CleanRoomApplicationCompiler` | one shared restore/build mechanism for generated templates and existing package-path applications |
| focused proof | `TemplatePackageCompilerTests` plus package-content clean room | exact bands, token failure, graph shape, install/create/restore/build/run |

## Sequencing correction

The current public package set is already recorded as incoherent, so a “public-to-candidate” rehearsal cannot start
from a working public application. Local artifacts are not public evidence. R08 therefore separates:

1. R08-04: compile and prove package-first candidate templates locally;
2. separately authorized initial coherent public wave and NuGet-only observation;
3. stage a later candidate and prove real public-to-candidate upgrade plus source/lockfile rollback;
4. explicit V1 decision.

This is stricter than the previous ordering and avoids manufacturing a rollback claim.

## Focused acceptance

1. No generated template source contains a hand-maintained Koan version line.
2. The template package graph names the exact bundles/connector it emits but ships no NuGet dependencies itself.
3. Packed template content contains canonical compatibility bands with selected/public floors and exclusive breaking
   ceilings; no unresolved token remains.
4. Installing the nupkg into an isolated template hive, creating both projects, and restoring/building/running only
   against the staged feed succeeds with business-visible assertions.
5. Missing floors, missing tokens, malformed versions, and incompatible output fail before a package can publish.
6. Focused tests/builds/docs/diff/privacy pass. No broad release ratchet or remote mutation runs.

## Implementation evidence

- Template source now owns only three package-intent tokens. The packaging compiler resolves selected/public
  floors and emits the canonical closed-open bands; direct `dotnet pack` fails before nupkg emission.
- Three standard suppressed ProjectReferences make bundle/SQLite changes impact the template release owner while
  the packed nupkg remains dependency-free.
- Focused materialization and real nupkg tests prove exact layout, no build output, no unresolved tokens, canonical
  ranges, and no NuGet dependency payload.
- The release clean room now installs the exact nupkg into an isolated CLI home, creates both public short names,
  discovers `dotnet new`'s output-derived project name, restores/builds, then proves console Entity results and a
  persisted web Todo through `EntityController<Todo>`.
- An executable attempt against the retained R05 staged feed correctly reached Koan application boot and rejected
  that historical feed: `Data.Core` expected `IBoundedQueryRepository<,>` while its packaged
  `Data.Abstractions` floor did not contain the type. That feed cannot be represented as current candidate proof.

The first exact checkout exposed two hidden ambient-state assumptions and the boundary repaired both rather than
weakening release invariants:

- template package tests now perform an explicit restore and then content-only `pack --no-build --no-restore`;
  they no longer depend on repository `obj` or Release reference assemblies;
- package discovery memoizes tracked/analyzer inputs only within one immutable inventory operation. A lineage
  compiler that evaluates an earlier commit and then applies a source delta cannot reuse stale Git facts.

The repaired focused boundary passes 66/66 template, product-surface, graph, planner, and repository-inventory
cells. A disposable exact projection of the complete intended checkout then compiled an initial 108-owner lineage,
planned all 108 independently versioned packages, and verified 108 nupkgs plus symbols. The same canonical
`pack --clean-room` operation installed `Sylin.Koan.Templates.0.17.610.nupkg`, created and ran both public template
shapes, persisted/queried a console `Todo`, built package-only FirstUse in 4.160s, and built package-only
GoldenJourney in 10.669s with zero compiler warnings or errors. No package, tag, GitHub Release, branch, or remote
configuration was published or mutated.

The long initial bootstrap remained correct but quiet while child pack output was buffered; PMC-006 already owns
that operator-progress polish. The successful console also exposed two framework collection failures in its startup
facts despite completing business work. The post-closure repair is now owned by
[ARCH-0119](../../../../decisions/ARCH-0119-one-console-host-lifecycle.md): `StartKoan()` uses one standard Generic
Host lifecycle, focused owner/consumer proof is green, and PMC-029 retains only exact next-candidate confirmation.

## Constraints satisfied

- Standard .NET template/NuGet/ProjectReference vocabulary only; no application API or Koan template option.
- No repository knowledge or version decision leaks to the application developer.
- No second release graph, version catalog, script, or operator input.
- Template application source remains business-readable; packaging complexity is centralized once.
- The probe reuses the existing process runner, clean-room feed, and web application host; it does not create a
  second execution or package-resolution mechanism.

## Risks and stop conditions

- Stop if the template package acquires runtime NuGet dependencies.
- Stop if every dependency patch unnecessarily mints a new template; bands change only at the breaking ceiling/floor
  required by a newly built template.
- Stop if direct `dotnet pack` can ship unresolved tokens silently.
- Stop if a local feed is described as public upgrade/rollback evidence.
- Stop before publication, trusted-publishing configuration, push, tag, Release, or remote mutation.
