# Koan packaging technical contract

## Authority

Git and evaluated MSBuild projects are authoritative. Every packable project under `src/`,
`packaging/`, or the top-level template package owns a project-local `version.json`. Evaluated
`ProjectReference`s form one `PackageGraph`; no XML parser or maintained package list participates.
Each package also receives a conservative shared-input map: known repository/ancestor build policy
plus evaluated packed inputs outside its owner directory. Known paths remain as tombstones when
deleted. Arbitrary external inputs discovered only through current evaluation are not retained in the
next lineage state, so their later deletion or rename is not certified until
[PMC-017](../../docs/initiatives/koan-v1/POST-CYCLE-TODO.md#current-register) is resolved or the path is
promoted into the known map.

The human signal is a compatibility-tier change in `version.json`. Before 1.0, a minor advance is
breaking; from 1.0 onward, a major advance is breaking.

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
selected dependency's range floor must equal the selected dependency identity.

During a same-source replay, every selected identity is packed, including an already-public identity.
This lets publication reconcile its symbol artifact and state without minting replacement bits.
Publication consumes the exact manifest/artifact directory and keys `release-state.json` to
`VersionCommit`.

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
and publication timeouts are fatal and name the relevant commit, package, or path.
