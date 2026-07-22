---
type: DEV
domain: framework
title: "NuGet publishing"
audience: [maintainers, release-engineers]
status: current
last_updated: 2026-07-20
framework_version: v0.20.0
---

# NuGet publishing

Koan publishes through one GitHub Actions workflow after source reaches `main`. Development commits
and open pull requests do not publish packages.

## Prerequisite

The Actions secret `NUGET_API_KEY` available to the repository must contain the existing nuget.org
publish key. No OIDC configuration, release branch, GitHub Release setting, tag convention, or remote
state store is required.

## Publish

1. Open a pull request targeting `main`. The cheap PR coherence job checks product/API truth, one
   Release build, lockfiles, and structural documentation/tooling drift; affected behavior tests
   remain part of developing and reviewing the change.
2. Merge the pull request. A deliberate direct commit to `main` has the same release effect.
3. Observe **Release packages** on the resulting `main` commit.

The one job:

1. checks out full Git history from `main` so NBGV can calculate package versions;
2. compiles the product surface, requiring local `version.json` ownership and exact agreement between
   supported claims and the 0.20 package closure;
3. packs the solution and the packable template project with `PublicRelease=true`; and
4. pushes only the selected 0.20 nupkgs with `--skip-duplicate`.

NuGet package identities are immutable. Rerunning the failed workflow run skips identities already
present and attempts the remaining packages. A missing key, invalid version owner, pack failure, or
push failure stops the job. A selected package with zero or multiple matching artifacts also stops
before that identity is pushed.

## Version changes

Ordinary changes mint the next patch through Git history. Change the `version` major/minor in the
owning project's `version.json` only when changing its compatibility tier. See
[Package versioning](versioning.md).

## Local preview

```powershell
dotnet nbgv get-version -p src/Koan.Core --public-release=true
dotnet run --project tools/Koan.Packaging -- inventory
```

Do not publish from a workstation, print the API key, or create a parallel package list.

The governing decisions are [ARCH-0110](../decisions/ARCH-0110-main-release-boundary.md) for the
publisher and [ARCH-0121](../decisions/ARCH-0121-claim-scoped-validation.md) for validation boundaries.
