# Koan packaging technical contract

## Authority

Git and evaluated MSBuild projects are authoritative. Every packable project under `src/`,
`packaging/`, or the top-level template package owns a project-local `version.json`. The release
compiler compares the public NBGV version at the push event's before and after commits. A changed
version is a touched package; an unchanged identity is reconciled only when absent from nuget.org.

## Artifact contract

`release-set.json` records the source commit, individual versions, topological project dependencies,
package dependencies, artifact names, and SHA-256 values. Packing is fail-fast, enables transitive
NuGet audit, and rejects high or critical advisories. Publication consumes this exact manifest and
artifact directory and records progress in `release-state.json`.

## Clean room

The verifier builds FirstUse and GoldenJourney in temporary directories outside the checkout. All
`Sylin.Koan*` packages are source-mapped to a hydrated local feed containing the release artifacts
and their public Koan closure. FirstUse protects the shortest result. GoldenJourney protects
persistence, durable Jobs progress and composition facts, operator/agent fact convergence, bounded
custom tools, honest dry-run behavior, and unavailable-adapter rejection/recovery. The two evidence
files remain separate so a release cannot hide a front-door regression behind the larger journey.

## Failure behavior

Missing version ownership, duplicate IDs, dependency cycles, absent internal dependency floors,
package metadata defects, non-canonical internal ranges, audit failures, clean-room failures, and
publication timeouts are fatal and name the affected package identity. Package publication from
`dev` is dependency-first and retried with
bounded backoff.
