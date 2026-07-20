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

Koan publishes through one manually dispatched GitHub Actions workflow. Development pushes do not
publish packages.

## Prerequisite

The repository Actions secret `NUGET_API_KEY` must contain the existing nuget.org publish key. No
OIDC configuration, release branch, GitHub Release setting, tag convention, or remote state store is
required.

## Publish

1. Ensure the intended source is on `dev`.
2. In GitHub Actions, open **Release packages**.
3. Choose **Run workflow** on `dev`.

The one job:

1. checks out full Git history so NBGV can calculate package versions;
2. evaluates package inventory and requires a local `version.json` for every packable project;
3. packs the solution and the packable template project with `PublicRelease=true`; and
4. pushes every resulting nupkg with `--skip-duplicate`.

NuGet package identities are immutable. A rerun skips identities already present and attempts the
remaining packages. A missing key, invalid version owner, pack failure, or push failure stops the job.

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

The governing decision is [ARCH-0110](../decisions/ARCH-0110-dev-release-compiler.md).
