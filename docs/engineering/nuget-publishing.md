---
type: DEV
domain: framework
title: "NuGet publishing"
audience: [maintainers, release-engineers]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
---

# NuGet publishing

Every push that advances `dev`—direct or merged—is a release event. The
[release-on-dev workflow](../../.github/workflows/release-on-dev.yml) finishes any incomplete prior
package wave, derives independently versioned packages from Git, proves exact artifacts, and promotes
them without routine operator input.

## One-time setup

Before the first authorized public wave:

1. Verify that the repository Actions secret `NUGET_API_KEY` contains the established nuget.org
   publish credential. Scope it to package publication, rotate it through the normal maintainer
   process, and never print or copy its value into workflow inputs or logs.
2. Enable immutable Releases for the GitHub repository. The workflow token cannot read the
   administration-level repository setting, so this is an explicit one-time prerequisite. Promotion
   accepts terminal success only when GitHub reports the published Release as immutable.
3. Protect `dev`, the release workflow, and `automation/package-lineage-dev` according to the
   repository's release trust policy. Workflow code and immutable Release custody are the attestation
   boundary.

If `NUGET_API_KEY` is absent, the exact draft is not prepared, or the final Release is mutable, the
workflow fails red. Do not expose the key to proof/staging jobs or introduce mutable evidence as a
workaround.

This implementation cycle completed local failure simulations and workflow contract tests, but did
not perform a real NuGet publication or observe a real immutable Release. The first public run remains
a separately authorized operation after the prerequisites above are verified.

## Six permission boundaries

The workflow separates proof, GitHub mutation, and credential use into six jobs:

| Job | Permission boundary | Responsibility |
| --- | --- | --- |
| `prepare_prior` | contents read | Serialize the event; inspect, materialize, and prove any incomplete prior version wave. |
| `stage_prior` | contents write | Stage only the exact prior bundle and marker on its draft Release. |
| `promote_prior` | contents write + step-scoped API key | Recheck prepared prior escrow, then converge the prior wave from exact custody. |
| `prove_current` | contents read | Compile lineage in two read-only matrix lanes: certification runs the release ratchet while packages concurrently packs, clean-room tests, and builds current escrow. |
| `stage_current` | contents write | Persist the exact lineage candidate and stage the exact current draft escrow. |
| `promote_current` | contents write + step-scoped API key | Recheck prepared current escrow, then converge the current wave from exact custody. |

Build/test/pack work has neither `contents:write` nor `NUGET_API_KEY`. Staging has no NuGet
credential. Only the promotion step receives the secret, after the prepared-state gate. Promotion
consumes the previously built coordinator and handoff; it does not restore, compile, test, or rebuild
source.

## What happens

1. `prepare_prior` waits for every earlier active `dev` release event. Serialization happens before
   version calculation.
2. It inspects the previous durable `VersionCommit`. A prepared prior wave is promoted first; a
   missing or markerless prior wave is reconstructed only while no selected nupkg is public. Public
   identity without exact prepared escrow fails closed.
3. `prove_current` applies the exact prior-source to current-source delta onto
   `automation/package-lineage-dev`. Bootstrap covers every owner once; later events select direct
   changes, breaking reverse dependents, mapped shared-input consumers, and current identities absent
   from nuget.org. Canonical bot identity plus the source commit's timestamp make that linear Git
   `VersionCommit` reproducible across the two isolated proof runners; staging rejects any disagreement.
4. Two isolated `prove_current` matrix lanes independently compile and verify the same exact lineage candidate.
   The certification lane runs the public-release ratchet: ordinary runnable test projects retain one process and
   hang detector per project while a processor-bounded wave executes up to four at once; the child-process-heavy
   Packaging suite then runs alone instead of contending with that wave. Concurrently, the packages lane compiles
   `release-set.json` from the same committed lineage truth, packs in dependency order, and runs generated templates,
   FirstUse, and GoldenJourney outside the checkout against only the staged feed. Both read-only lanes must pass
   before staging can begin.
5. `wave-bundle` creates `release-wave-<full-VersionCommit>.zip` containing lineage, manifest, all
   selected nupkg/snupkg files, and both application proofs. `release-wave.json` binds its exact hashes,
   package count, version commit, and `release/dev/<full-VersionCommit>`.
6. `stage_current` persists the exact lineage commit, creates one draft GitHub Release, uploads the
   ZIP first, and uploads the marker last. The uploaded marker becomes authority and is never replaced.
7. After a second prepared-state check, the promotion step receives `NUGET_API_KEY`. Promotion follows
   manifest dependency order, pushes a missing nupkg, always replays each required exact snupkg with
   duplicate-safe semantics, and waits until every nupkg is visible.
8. One deterministic `release-completion.json` binds the prepared marker, bundle, lineage, manifest,
   and package/symbol hashes. Promotion re-reads the complete draft, creates or verifies the
   non-forced full-commit tag, publishes the same draft, and requires GitHub to report it immutable.

There is no per-package remote checklist, abbreviated tag, or artifact rebuild after partial public
publication. The immutable Release is exact binary custody; NuGet is the package availability surface.

When no identity needs publication, lineage can still advance, but the workflow creates no
release-wave ZIP, draft Release, tag, or completion receipt.

## Happy path

```text
push or merge to dev
```

The workflow summary lists the source/version commits, breaking closure, generated markers, and each
selected package's previous version, resulting version, and reason. A completed non-empty wave has:

- a green `Release packages from dev` run;
- immutable Release assets containing exact lineage, manifest, packages, symbols, and application
  evidence;
- one exact completion receipt; and
- `release/dev/<full-VersionCommit>` targeted at that same full version commit.

No package selection, patch calculation, recovery choice, tag, or credential copy/paste belongs in
the happy path.

## Failure → recovery

### Build, test, pack, audit, or clean-room failure

Read the first failed step, fix the source/metadata/dependency/advisory, and advance or re-run `dev`.
No current-wave package artifact was published and no current-wave marker or tag was created. Prior
recovery may already have converged in the same run. The previous lineage remains durable; the failed
current candidate has not yet been persisted remotely.

### Draft staging stopped

A draft with no uploaded marker is `staging`. Automation may delete and rebuild that draft only after
confirming that none of its selected nupkgs is public. A marker left in GitHub's `starter` state is not
authority and follows the same reset path.

Once `release-wave.json` is uploaded, the draft is prepared authority. Missing, tampered, conflicting,
or unknown assets then fail closed; automation never replaces the marker or bundle. The exact
completion asset alone may be deleted and retried when GitHub left it in `starter` state.

### Package, symbol, visibility, or response failed

Re-run the workflow or let the next `dev` event start. `prepare_prior` inspects the prior full
`VersionCommit` before current compilation. It downloads the original escrow, skips an nupkg already
visible, always replays every required exact snupkg, waits for the entire manifest, and emits the one
completion receipt. No operator chooses a package, artifact path, or old commit.

An immutable published completion remains historical custody evidence. A later nuget.org outage or
package unlisting does not reinterpret that completed wave.

### nuget.org visibility times out

Retry after the registry recovers. If the push succeeded before the timeout, the next attempt observes
the nupkg, skips repushing it, replays exact symbols, and waits again.

### NuGet API key is absent or rejected

Verify the `NUGET_API_KEY` repository Actions secret and its nuget.org publish scope without printing
the value. Rotate the secret through the established maintainer process if nuget.org has revoked or
expired it; do not weaken escrow checks or pass the key to another job.

### Published Release is not immutable

Treat this as a failed release boundary, not success. Do not move the tag, replace assets, or create a
second Release. Verify the one-time repository setting before any first publication; if this failure is
ever observed, stop subsequent waves and review the remote state explicitly.

## Local diagnosis

```powershell
dotnet run --project tools/Koan.Packaging -- inventory
```

`inventory` is read-only. `lineage` creates and switches branches; reproduce compilation only in a
clean disposable checkout using the [packaging tool sequence](../../tools/Koan.Packaging/README.md).
`wave-stage` and `wave-promote` mutate remote state and are not local recovery commands.

## Anti-patterns

- Do not skip an individual `dev` release event or batch releases by commit-message convention.
- Do not pack every repository package and call that selection; pack the exact compiled set.
- Do not ignore a pack failure and publish the remaining files.
- Do not disable audit, edit lineage/manifest/marker evidence, or publish artifacts from another
  commit.
- Do not create, shorten, move, or force the release tag by hand.
- Do not replace uploaded release-wave assets or introduce a mutable per-package checklist.
- Do not calculate versions before the release queue lease or create merge commits on the linear
  package-lineage branch.
- Do not treat `SourceCommit` as package metadata; `VersionCommit` owns the shipped bits and tag.

See [versioning.md](versioning.md), [packaging.md](packaging.md), and
[ARCH-0110](../decisions/ARCH-0110-dev-release-compiler.md).
