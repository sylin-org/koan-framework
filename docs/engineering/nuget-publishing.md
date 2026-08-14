---
type: DEV
domain: framework
title: "NuGet publishing"
audience: [maintainers, release-engineers]
status: current
last_updated: 2026-08-14
framework_version: v1.0.0
---

# NuGet publishing

Koan integrates on `dev` and publishes only from an explicit `vX.Y.Z` tag whose commit is reachable
from `dev`. Reachability makes the tag eligible; the release workflow independently certifies the
tagged source before any package can publish. Branch pushes and pull requests cannot publish.

## Prerequisite

The repository Actions secret `NUGET_API_KEY` must contain the nuget.org publish key. Only the final
publish job receives it.

## Publish a train

1. Merge focused work to `dev` and resolve its ordinary coherence feedback.
2. Choose the exact `dev` commit and preview its public NBGV version:

   ```powershell
   dotnet nbgv get-version -p src/Koan.Core --public-release=true
   ```

3. Create and push the matching tag. For example:

   ```powershell
   git tag -a v1.0.0 <dev-sha> -m "Release 1.0.0"
   git push origin v1.0.0
   ```

4. Observe **Release packages**. Do not publish from a workstation.

The workflow rejects a tag that is not reachable from `origin/dev` or does not equal the stable NBGV
package version for that commit. It then builds the Release solution and template on the tagged SHA
before compiling the package inventory and packing.

## What the workflow proves

The first job has no NuGet credential. It checks the evaluated inventory, builds the public release,
packs every active package once, verifies the complete local feed, runs the package-only App/JSON
consumer, hashes the inventory plus package files, and uploads the result.

The publish job downloads that artifact, verifies every hash, derives a dependency-topological order
from the certified inventory, and pushes dependencies before dependents. Its first attempt verifies
that all primary identities are absent before pushing any package. If that attempt is interrupted,
a rerun accepts an existing primary only when every original ZIP entry name and uncompressed byte
hash matches the certified package; nuget.org's added `.signature.p7s` is the sole excluded entry.
Certified `.snupkg` sidecars are pushed separately after all primaries. The job cannot rebuild or
widen the inventory.

## Failure and recovery

- For a transient registry or credential failure, correct the external cause and rerun only the
  failed publish job. It reuses and re-hashes the certified artifact, verifies the content of any
  primary already on nuget.org, and retries the certified symbol sidecars.
- For a source, inventory, package, or consumer failure, merge a correction to `dev` and create the
  new version's tag. Do not move a tag whose artifacts may have been published.
- If local hashes, remote package content, dependency closure, or artifact selection differ,
  publication stops rather than accepting a mixed train.

There is no `dev`-to-`main` promotion, release branch, parallel package list, or repack-on-publish
path. See [Package versioning](versioning.md) and
[ARCH-0124](../decisions/ARCH-0124-single-package-release-train.md).
