---
type: WORK
domain: release
status: active
last_updated: 2026-07-20
framework_version: v0.20.0
---

# R12-06 — Publish the first 0.20 package wave

## Outcome

Publish correctly versioned NuGet packages through one explicit GitHub Actions job. Release
automation must not become a second product.

## Bare-bones release checkpoint — 2026-07-20

**Task:** Replace the automatic release compiler with one explicit, one-job NuGet publication path
owned by the `main` integration boundary.

**Application intent:** A maintainer merges a pull request into `main` or commits directly to `main`;
the resulting `main` commit publishes the repository's independently versioned packages.

**Public expression:** GitHub validates pull requests targeting `main`; merging creates a `main` push
that runs `Release packages`. Every packable project owns a local `version.json`;
Nerdbank.GitVersioning computes its public package version; the established `NUGET_API_KEY` is the
only credential.

**Guarantee/correction:** Only source present on `main` can receive the NuGet credential. The workflow
evaluates the repository's packable projects, packs each with `PublicRelease=true`, and pushes the
resulting packages to nuget.org. A missing API key, invalid or missing version owner, restore/pack
failure, or NuGet push failure stops the single job. Existing immutable package identities are
skipped by NuGet; rerun the failed `main` workflow after correcting the owner.

**Complete intent surface:** Change a package's major/minor in its own `version.json` only when its
compatibility tier changes; open and merge a pull request to `main` (or deliberately commit directly
to `main`); read the ordinary job result. No manual dispatch, branch selector, package checklist,
lineage seed, escrow preparation, tag operation, recovery state, or remote configuration is part of
the release expression.

**Public concepts:** Standard GitHub Actions pull-request and push events, standard .NET
restore/pack/NuGet push, and the existing NBGV `version.json` format. No release-specific Koan
concept is exposed.

**Docs read:**

- `docs/engineering/index.md` establishes the repository guardrails; relevant because it previously
  required the release compiler.
- `docs/architecture/principles.md` requires standard .NET concepts and fewer owners; directly
  supports deleting the parallel release state machine.
- `docs/engineering/nuget-publishing.md` describes the current six-job path; it must be replaced.
- `docs/engineering/versioning.md` establishes project-local version ownership; that valuable rule
  remains.
- `docs/engineering/packaging.md` establishes evaluated packability and package metadata; those
  package-shape rules remain while release-wave policy is removed.
- `docs/decisions/ARCH-0110-main-release-boundary.md` owns the corrected integration and publication
  boundary.

**Code read:**

- `.github/workflows/release-on-main.yml` is one `main`-push job; its predecessor was rebuilt from a
  773-line, six-job state machine and briefly exposed an incorrect manual-from-`dev` boundary.
- `tools/Koan.Packaging/Program.cs` exposes lineage, planning, clean-room packing, wave staging, and
  promotion commands; retain only inventory/product assessment commands.
- `tools/Koan.Packaging/Services/RepositoryInspector.cs` evaluates packability and local version
  ownership; retain it as the one package discovery chokepoint.
- `tools/Koan.Packaging/Services/PackageGraph.cs` provides ordinary evaluated dependency structure to
  product assessment; retain it.
- `Directory.Build.props` and `Directory.Build.targets` attach NBGV and bounded internal dependency
  ranges; retain those version and package semantics.

**Reusing:** Existing package-local `version.json` files, NBGV stamping, evaluated package inventory,
standard package metadata and compatibility-range targets, nuget.org, and `NUGET_API_KEY`.

**Creating new:**

| New code | Location | Justification |
| --- | --- | --- |
| None | — | The release is expressible with existing .NET and GitHub Actions concepts. |

**Coalescence:** The closest pattern is the current workflow, but its disposition is **rebuild**.
MSBuild/NBGV remain the version owner; the single workflow job becomes the release owner. Delete the
lineage compiler, automation branch, manifests, release-wave escrow, GitHub Release coordinator,
generated application proof harness, recovery model, and their release-specific tests. A broader
release platform is wrong because NuGet already owns immutable package publication; a narrower
per-package script is wrong because evaluated repository inventory already identifies the complete
packable surface.

**Ergonomics:** One familiar action—merge to `main`—one job, one log, and one version file per
package. There is no manual branch choice, hidden remote prerequisite beyond the existing API key, or
second Git history to understand.

**Constraints satisfied:**

- Standard .NET, NuGet, NBGV, and GitHub Actions concepts are primary.
- Per-project versions remain independent.
- No runtime, data, controller, Entity, provider, or public framework API changes.
- No new constants, options, DTOs, services, branches, tags, or ledgers.
- Historical implementation detail remains available in Git history rather than current guidance.
- Validation is limited to building the small packaging tool and checking the workflow's shape.

**Risks:** nuget.org does not provide an atomic multi-package transaction. The minimal correction is
to fix the cause and rerun the workflow: immutable identities are skipped and missing identities are
pushed. This is sufficient for the first pre-release and avoids maintaining a speculative transaction
system inside Koan.

## Work

1. Publish only on push to `main`; validate pull requests to `main`; trigger nothing from `dev`.
2. Pack every evaluated packable project with its NBGV public version.
3. Push the resulting nupkgs with the established API key and duplicate-safe NuGet semantics.
4. Remove the unused lineage, escrow, coordinator, recovery, and clean-room release implementation.
5. Realign current engineering guidance and mark the former compiler decision superseded.
6. Build only `Koan.Packaging` and perform a static workflow check. Do not run the release ratchet.

## Acceptance

1. Release is one `main`-push workflow, one job, and one API key; `dev` activity does nothing.
2. Every packable project is discovered through evaluated MSBuild state and owns `version.json`.
3. NBGV remains the sole package/assembly version source and `PublicRelease=true` removes local
   build suffixes.
4. The workflow restores, packs, and pushes; it does not test the repository, create Git history,
   stage escrow, create tags/Releases, or mutate repository configuration.
5. Current docs teach only this path.

## Implementation evidence — 2026-07-20

- The workflow contains one `main`-push job, one API-key reference, no test command, no Git mutation,
  and no lineage/release-wave state. PR validation remains separate and cannot access publication.
- The surviving package inventory tool builds cleanly and evaluates 93 independently versioned
  packages.
- Eighteen supported product claims resolve to exactly 38 guaranteed package owners; all 38 declare
  `versionIntent=0.20`. The remaining 55 packages preserve their lower maturity versions.
- The packaging test project compiles after removal of the release-specific source set; tests were
  deliberately not executed.
- Three manual-from-`dev` attempts failed before publication and one credential retry was cancelled
  during packing; no attempt published a package. The manual path is now removed.

## Standard template-pack checkpoint — 2026-07-20

**Task:** Make `Sylin.Koan.Templates` ordinary, directly packable NuGet content after removal of the
release compiler.

**Application intent:** A maintainer runs standard `dotnet pack`; users install the template package
and receive generated projects compatible with the guaranteed Koan 0.20 family.

**Public expression:** Generated projects contain ordinary bounded NuGet `PackageReference` versions
`[0.20.0,0.21.0)`.

**Guarantee/correction:** NuGet resolves an available compatible 0.20 package and rejects 0.21 or
later. Template packing fails only for ordinary MSBuild/NuGet errors, not because a Koan-only compiler
failed to prepare hidden content.

**Complete intent surface:** Pack the content-only project directly. No prepared template root,
token replacement, release manifest, ProjectReference impact marker, or template-specific release
command remains.

**Public concepts:** Standard NuGet compatibility ranges and standard content-only template packing.

**Coalescence:** Rebuild the template package as self-contained source. Delete the prepared-root
content fork, unresolved range tokens, compiler-required error target, and suppressed release-impact
ProjectReferences. The template project is the one owner because these ranges are part of the source
it ships; release tooling is too broad and generated applications are too late.

**Ergonomics:** One `dotnet pack` command and immediately readable generated project files. A future
0.21 template deliberately changes its visible ranges and receives a new template identity through
its existing local NBGV owner.

## Authorization boundary

The maintainer authorized redesign and local implementation. Commit the correction to `dev`, where it
triggers nothing. Do not update `main`, publish from a workstation, create a tag/Release, or change
remote configuration while validating this correction; merging to `main` is the publication action.
