# Koan packaging technical contract

## Authority

Git and evaluated MSBuild projects are authoritative. Every packable project under `src/`,
`packaging/`, or the top-level template package owns a project-local `version.json`. Evaluated
`ProjectReference`s form one `PackageGraph`; no XML parser or maintained package list participates.
Evaluated package consumers also record shared build/pack inputs outside their owner directory, so a
Git change to repository or ancestor build policy cannot silently reuse an unaffected path height.

The human signal is a compatibility-tier change in `version.json`. Before 1.0, a minor advance is
breaking; from 1.0 onward, a major advance is breaking.

## Durable lineage

Release events serialize before version calculation. The compiler starts at the previous durable
version commit and applies exactly the Git tree delta between the previous and current `dev` source
commits. This creates a linear projection rather than a merge graph, so NBGV path heights remain
package-independent.

The first lineage is a deliberate all-owner bootstrap. Later waves include the breaking reverse
closure plus packages whose evaluated shared inputs changed. NBGV identities are calculated from a
detached checkout of each exact commit, never from the current working tree with a revision hint.

For every breaking root, the graph computes its complete transitive reverse closure. After a
provisional projection commit exposes the actual NBGV identities, only closure members that still
match their previous identity receive a deterministic package-local marker. The commit is amended,
and every closure member is checked again for a fresh identity. Source-owned reserved state/marker
paths, deletion/rename, non-forward source, graph cycles, and incomplete identity waves fail closed.
Each lineage commit must have exactly the recorded single parent and, after removing generated
state/markers, a tree byte-identical to `SourceCommit`. Manual branch commits and source-continuity
drift are rejected before the next projection.

`.koan-package-lineage.json` is committed on `automation/package-lineage-dev`. The external
`release-lineage.json` adds the resulting `VersionCommit`; planning rejects it unless every field
matches the committed state.

## Artifact contract

`SourceCommit` is audit intent. `PreviousVersionCommit` and `VersionCommit` are version truth.
`release-set.json` records those identities, breaking causes, dependency order, package dependencies,
artifact names, and SHA-256 values. Packing requires `HEAD == VersionCommit`, validates package
repository metadata against that commit, enables transitive NuGet audit, and rejects high or critical
advisories. The packed Koan dependency set must equal the evaluated `ProjectReference` graph, and a
selected dependency's range floor must equal the selected dependency identity.

Every selected identity is packed, including an already-public identity selected during replay. This
lets publication reconcile its symbol artifact and state without minting replacement bits. Publication
consumes the exact manifest/artifact directory and keys `release-state.json` to `VersionCommit`.

## Clean room

The verifier builds FirstUse and GoldenJourney in temporary directories outside the checkout. All
`Sylin.Koan*` packages are source-mapped to a hydrated local feed containing the release artifacts and
their public Koan closure. Separate evidence files prevent the larger journey from hiding a shortest-
path regression.

## Failure behavior

Missing version ownership, duplicate IDs, dependency cycles, lineage drift, reserved-path collisions,
unsupported package moves, non-forward source, stale closure identities, wrong-checkout packing,
dirty package inputs, absent or mismatched internal dependency floors, metadata defects, non-canonical
ranges, audit failures, clean-room failures, and publication timeouts are fatal and name the relevant
commit, package, or path.
