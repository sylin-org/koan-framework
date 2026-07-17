# Koan packaging technical contract

## Authority

Git and evaluated MSBuild projects are authoritative. Every packable project under `src/`,
`packaging/`, or the top-level template package owns a project-local `version.json`. Evaluated
`ProjectReference`s form one `PackageGraph`; no XML parser or maintained package list participates.
Each package also receives a conservative shared-input map: known repository/ancestor build policy,
Git-tracked sources of analyzer ProjectReferences, and explicit evaluated `KoanPackageInput` source
outside its owner directory. Known paths remain as tombstones when deleted. Lineage schema 3 persists
the normalized evaluated map for every owner; impact is calculated against the previous and current
maps together. A source-generating analyzer therefore selects each consuming package automatically;
an external generated payload keeps its explicit owner through add, change, delete, and rename.
Generated `bin`/`obj`, ignored artifacts, untracked files, and owner-local files cannot establish
shared-input ownership. Analyzer source inventories are Git-backed and cached once per project during
inventory, so local build history cannot change release intent.

The human signal is a compatibility-tier change in `version.json`. Before 1.0, a minor advance is
breaking; from 1.0 onward, a major advance is breaking.

## Package-product quality projection

`PackageQualityCompiler` consumes the same `PackageProject` inventory and `PackageGraph`; it owns no
package list or release state. Standard artifact shape is centralized in `PackageClassifier` and
shared with `ProductSurfaceCompiler`. Semantic presentation roles derive from ordinary package names,
shape, and graph facts. An ambiguous role is an R11 package-boundary finding, not permission to add a
parallel role attribute.

The compiler reads only evaluated metadata and package-owned companion files. Its findings distinguish
objective errors from human-review signals; heading detection cannot promote prose quality, maturity,
or support. JSON and Markdown order are deterministic. The `quality` command performs no lineage,
version, pack, registry, release, or remote operation.

## Durable lineage

Release events serialize before version calculation. The compiler starts at the previous durable
version commit and applies exactly the Git tree delta between the previous and current `dev` source
commits. This creates a linear projection rather than a merge graph, so NBGV path heights remain
package-independent.

The first lineage is a deliberate all-owner bootstrap. It evaluates the predecessor's package
inventory with the pinned toolchain, so that predecessor must remain evaluable. Later waves include
the breaking reverse closure plus packages whose mapped shared inputs changed. Every committed
lineage state records the exact public NBGV identity of every package owner. After bootstrap, later
waves use those durable identities as their previous truth and calculate only the checked-out
provisional/final commit; they never reinterpret an old release with today's SDK, tool version, or
working tree.

For every breaking root, the graph computes its complete transitive reverse closure. After a
provisional projection commit exposes the actual NBGV identities, only closure members that still
match their previous identity receive a deterministic package-local marker. The commit is amended,
and every closure member is checked again for a fresh identity. A deleted owner becomes a permanent
lineage retirement: its final ID/path/version remain reserved and no new artifact is planned. Reuse,
rename, source-owned reserved state/marker paths, non-forward source, graph cycles, and incomplete
identity waves fail closed.
Each lineage commit must have exactly the recorded single parent and, after removing generated
state/markers, a tree byte-identical to `SourceCommit`. Manual branch commits and source-continuity
drift are rejected before the next projection.

`.koan-package-lineage.json` is committed on `automation/package-lineage-dev`. The external
`release-lineage.json` adds the resulting `VersionCommit`; both include the complete package/version
inventory, and planning rejects the artifact unless every field matches committed state and current
NBGV calculation.

## Artifact contract

`SourceCommit` is audit intent. `PreviousVersionCommit` and `VersionCommit` are version truth.
`release-set.json` records those identities, breaking causes, dependency order, package dependencies,
artifact names, and SHA-256 values. Packing requires `HEAD == VersionCommit`, validates package
repository metadata against that commit, enables transitive NuGet audit, and rejects high or critical
advisories. The packed Koan dependency set must equal the evaluated `ProjectReference` graph, and a
selected dependency's range floor must equal the selected dependency identity. The verifier also
requires the declared package-owned README to exist in the nupkg and requires `icon.png` to match the
repository mascot byte-for-byte; evaluated metadata cannot substitute for packed-content proof.

The manifest package set must be an exact subset of active committed lineage by package ID, project
path, and version; bootstrap state must also match. An artifact cannot therefore introduce an identity
that the committed version projection did not authorize.

Before any identity is public, a missing or markerless wave may be proved and packed again at its exact
`VersionCommit`. Once an identity is public, recovery never relies on rebuilding: the original verified
nupkg/snupkg bytes must come from durable release-wave escrow.

## Durable release wave

`ReleaseWaveBundle` writes a deterministic, uncompressed
`release-wave-<full-VersionCommit>.zip`. Its exact entry set is lineage, manifest, all manifest-
referenced nupkg/snupkg files, and the separate FirstUse and GoldenJourney evidence. Entry order,
timestamps, names, lengths, and SHA-256 values are canonical. `release-wave.json` is written last and
binds the ZIP, inner lineage/manifest hashes, package count, source/version commits, and
`release/dev/<full-VersionCommit>` tag.

The ZIP is uploaded to one draft GitHub Release first; the standalone marker is uploaded last. State
is derived from that Release rather than persisted in a second ledger:

- `missing`: no draft or published Release uses the exact tag;
- `staging`: a draft has no uploaded marker and can be reset only while no selected nupkg is public;
- `prepared`: the draft's uploaded marker and bundle validate byte-for-byte;
- `published`: the same Release is immutable, its completion receipt validates, and its non-forced tag
  resolves to the full `VersionCommit`.

An uploaded marker is authority. Missing, tampered, conflicting, unknown, or starter assets beneath
that authority fail closed. The sole repairable exception is an exact `release-completion.json` left
in GitHub's `starter` state: the GitHub adapter deletes only that incomplete asset, reconciles response
loss, and uploads the exact receipt. Uploaded assets are never clobbered.

Promotion downloads and revalidates escrow into an isolated scratch directory. In manifest dependency
order it pushes only a missing nupkg and always replays every required snupkg using duplicate-safe
semantics. It then waits for every nupkg, writes one deterministic receipt binding the marker, bundle,
lineage, manifest, and package/symbol hashes, re-reads the complete draft, and publishes that same
Release. The receipt records the visibility and symbol-replay result at completion; later registry
outage or unlisting does not reinterpret immutable historical custody. No per-package snapshot or
per-package recovery ledger participates.

Before compiling the current source wave, the workflow inspects and converges the prior
`VersionCommit`. Exact prior escrow is therefore recovered before a later lineage event can promote.
If a selected nupkg is public without prepared escrow, automation blocks; it never mints or rebuilds
replacement bits under that identity. Empty manifests create no bundle, draft, tag, or receipt.

## Workflow trust boundaries

The workflow has six explicit jobs:

1. `prepare_prior` has read-only contents permission and inspects or reconstructs proof for the prior
   exact version wave.
2. `stage_prior` has contents write permission and stages only prior escrow.
3. `promote_prior` has OIDC permission but exchanges identity only after prepared escrow is rechecked,
   then converges the prior wave.
4. `prove_current` has read-only contents permission and compiles, tests, packs, and bundles current
   exact inputs.
5. `stage_current` has contents write permission to persist the exact lineage candidate and stage the
   current draft escrow.
6. `promote_current` has OIDC permission but exchanges identity only after prepared current escrow is
   rechecked, then promotes the current wave.

Build, test, pack, and clean-room proof never receive `contents:write` or `id-token:write`. Staging
never receives an OIDC credential. Promotion jobs consume the previously built coordinator and exact
handoff; they do not restore, compile, test, or rebuild source.

## Clean room

The verifier builds FirstUse and GoldenJourney in temporary directories outside the checkout. All
`Sylin.Koan*` packages are source-mapped to a hydrated local feed containing the release artifacts and
their public Koan closure. Separate evidence files prevent the larger journey from hiding a shortest-
path regression.

All subprocesses launched through the packaging runner disable reusable MSBuild worker nodes. This
keeps redirected output handles owned by the immediate child, so build, restore, evaluation, and pack
commands reach observable completion instead of depending on build-server lifetime.

## Failure behavior

Missing version ownership, duplicate IDs, dependency cycles, lineage drift, reserved-path collisions,
unsupported package moves, non-forward source, stale closure identities, wrong-checkout packing,
dirty package inputs, absent or mismatched internal dependency floors, metadata defects, non-canonical
ranges or version intent, an unevaluable bootstrap predecessor, audit failures, clean-room failures,
oversized or hostile escrow, duplicate Releases/assets, wrong tag targets, missing prepared authority,
public identities without escrow, mutable published Releases, and publication timeouts are fatal and
name the relevant commit, package, asset, or path.

Create/delete/upload/push/visibility/publish response loss is reconciled from observed external state.
Exact symbols are replayed on every nonterminal promotion attempt. A published immutable completion is
historical custody evidence and is not reopened because the registry is later unavailable.

## Maturity and schema obligation

The local implementation, adversarial simulations, and least-privilege workflow contracts are
complete. No real NuGet publication or immutable GitHub Release was observed in this implementation
cycle. Immutable Releases and NuGet trusted publishing are one-time repository prerequisites for the
separately authorized first public wave.

Completion schema 1 is a durable byte contract. Before adding or reordering receipt fields, introduce
schema-dispatched readers and a frozen golden schema-1 fixture so an older immutable Release remains
inspectable and an older prepared draft remains recoverable.
