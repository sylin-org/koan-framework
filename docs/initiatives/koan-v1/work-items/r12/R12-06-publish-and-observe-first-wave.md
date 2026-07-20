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

**Task:** Replace the automatic release compiler with one explicit, one-job NuGet publication path.

**Application intent:** A maintainer deliberately says, “Publish the repository's independently
versioned packages now.”

**Public expression:** Run the `Release packages` workflow from `dev`. Every packable project owns a
local `version.json`; Nerdbank.GitVersioning computes its public package version; the established
`NUGET_API_KEY` is the only credential.

**Guarantee/correction:** The workflow evaluates the repository's packable projects, packs each with
`PublicRelease=true`, and pushes the resulting packages to nuget.org. A missing API key, invalid or
missing version owner, restore/pack failure, or NuGet push failure stops the single job. Existing
immutable package identities are skipped by NuGet instead of being rebuilt into a recovery protocol.

**Complete intent surface:** Change a package's major/minor in its own `version.json` only when its
compatibility tier changes; dispatch the workflow from `dev`; read the ordinary job result. No branch
advancement, package checklist, lineage seed, escrow preparation, tag operation, recovery state, or
remote configuration is part of the release expression.

**Public concepts:** Standard GitHub Actions manual dispatch, standard .NET restore/pack/NuGet push,
and the existing NBGV `version.json` format. No release-specific Koan concept is exposed.

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
- `docs/decisions/ARCH-0110-dev-release-compiler.md` owns the superseded release compiler decision and
  will be amended to the simpler operator contract.

**Code read:**

- The former `.github/workflows/release-on-dev.yml` was a 773-line, six-job state machine; replace it with one
  manually dispatched job.
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

**Ergonomics:** One deliberate workflow action, one job, one log, and one version file per package.
There are no hidden remote prerequisites beyond the existing API key and no second Git history to
understand.

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

1. Replace automatic `dev` publication with manual workflow dispatch restricted to `dev`.
2. Pack every evaluated packable project with its NBGV public version.
3. Push the resulting nupkgs with the established API key and duplicate-safe NuGet semantics.
4. Remove the unused lineage, escrow, coordinator, recovery, and clean-room release implementation.
5. Realign current engineering guidance and mark the former compiler decision superseded.
6. Build only `Koan.Packaging` and perform a static workflow check. Do not run the release ratchet.

## Acceptance

1. Release is one explicit workflow, one job, and one API key.
2. Every packable project is discovered through evaluated MSBuild state and owns `version.json`.
3. NBGV remains the sole package/assembly version source and `PublicRelease=true` removes local
   build suffixes.
4. The workflow restores, packs, and pushes; it does not test the repository, create Git history,
   stage escrow, create tags/Releases, or mutate repository configuration.
5. Current docs teach only this path.

## Implementation evidence — 2026-07-20

- The workflow is 58 lines, manual-only, and contains one job, one API-key reference, no test command,
  no Git mutation, and no lineage/release-wave state.
- The surviving package inventory tool builds cleanly and evaluates 93 independently versioned
  packages.
- Eighteen supported product claims resolve to exactly 38 guaranteed package owners; all 38 declare
  `versionIntent=0.20`. The remaining 55 packages preserve their lower maturity versions.
- The packaging test project compiles after removal of the release-specific source set; tests were
  deliberately not executed.
- No workflow was dispatched and no package or remote state was changed.

## Authorization boundary

The maintainer authorized redesign and local implementation. Do not dispatch the workflow, publish a
package, create a tag/Release, push, or change the remote API key/configuration during this work item.
