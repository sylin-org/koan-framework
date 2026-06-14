# Versioning

How Koan package versions are computed and how to control them. Versions are driven by Nerdbank.GitVersioning (nbgv) per ARCH-0085.

> Companion ADR: [ARCH-0085](../decisions/ARCH-0085-versioning-compatibility-and-automation.md) -- why the system is shaped this way.
> Companion ADR: [ARCH-0082](../decisions/ARCH-0082-versioning-strategy.md) -- the strategy this supersedes.
> Companion workbook: [nuget-publishing.md](nuget-publishing.md) -- once versions are decided, how they reach nuget.org.

---

## When to use this

- You're preparing a commit and want to know what version it'll produce
- A package bumped (or didn't bump) and you want to understand why
- You're adding a new package and need to wire it into the versioning system
- You want to bump a package's major or minor version deliberately

**Prerequisites:**

- PowerShell 7+ (`pwsh`), Git, .NET 10 SDK
- `dotnet tool restore` at the repo root (installs `nbgv`)
- Working tree at the repo root

---

## Mental model (30 seconds)

Every packable project under `src/` has its own `version.json` file with:

```json
{
  "version": "0.17",
  "versionHeightOffset": -1,
  "pathFilters": ["."]
}
```

- **`version`** -- the major.minor floor. You own this; edit it to bump major or minor.
- **patch** -- computed automatically by nbgv as the git commit height of that package's folder since `version` last changed. A folder with no new commits since its last published version keeps the same patch; nuget.org's `--skip-duplicate` skips it on push.
- **`versionHeightOffset: -1`** -- ensures the commit that introduces or changes `version.json` lands on `major.minor.0` (not `.1`).
- **`pathFilters: ["."]`** -- only commits touching THIS package's folder increment its patch. A commit touching only `src/Koan.Cache.Adapter.Redis/` does not bump `src/Koan.Core/`.

Clean versions (no `-gXXXXXXX` prerelease suffix) require being on `main` or passing `-p:PublicRelease=true`.

Releases are tagged `release/YYYY-MM-DD` by the workflow. Tags are for audit trail only -- they do not drive version computation.

That's the whole model. Everything else in this workbook is how to operate on it.

---

## Happy path

You don't operate the versioning system on every commit. The CI workflow does it on every merge to `main` -- see [nuget-publishing.md](nuget-publishing.md) for that flow.

To **preview** what version a package will get on the next release:

```pwsh
# From the repo root, for a specific package:
dotnet nbgv get-version -p src/Koan.Cache.Adapter.Redis

# Or from inside the package folder:
cd src/Koan.Cache.Adapter.Redis
dotnet nbgv get-version

# To see the NuGet package version specifically:
dotnet nbgv get-version -p src/Koan.Cache.Adapter.Redis --format json | ConvertFrom-Json | Select-Object NuGetPackageVersion
```

If the output shows a prerelease suffix (e.g., `0.17.5-g1a2b3c4`), that is correct for a non-main branch. The suffix is stripped when building from `main` or with `-p:PublicRelease=true`.

---

## Scenarios

| If you want to... | Go to |
|---|---|
| Fix a bug in one package | [Patch bump (automatic)](#patch-bump-automatic) |
| Bump a package to a new minor or major | [Minor or major bump](#minor-or-major-bump) |
| Add a brand-new package | [Adding a package](#adding-a-package) |
| Temporarily exclude a package from NuGet | [Mark a package non-packable](#mark-a-package-non-packable) |
| Run a release manually (CI is down) | See [nuget-publishing.md -- Manual publish](nuget-publishing.md#manual-publish) |
| Preview the version without committing | [Preview version](#preview-version) |

### Patch bump (automatic)

Commit any code change to the package's folder. The patch number increments by one for each commit that touches the folder since the last `version.json` change.

```pwsh
# Commit your change normally.
git commit -m "fix(cache): Redis adapter stalls on slow Subscribe"

# Preview the resulting version.
dotnet nbgv get-version -p src/Koan.Cache.Adapter.Redis
# Expect: Version = 0.17.X where X is one more than last published.

# Push and open a PR to main. CI handles the rest.
```

### Minor or major bump

Edit the package's `version.json` `version` field and commit that file as part of your change:

```pwsh
# Example: bump Koan.Cache.Adapter.Redis from 0.17 to 0.18.
# Edit src/Koan.Cache.Adapter.Redis/version.json:
#   "version": "0.18"

git add src/Koan.Cache.Adapter.Redis/version.json
git commit -m "feat(cache): Redis adapter 0.18 -- streaming subscribe support"

# Preview: patch resets to 0 for this commit (versionHeightOffset = -1).
dotnet nbgv get-version -p src/Koan.Cache.Adapter.Redis
# Expect: NuGetPackageVersion = 0.18.0 (on main / with PublicRelease=true)
```

For a **major bump** that signals a breaking change, increment the major digit instead:

```json
{
  "version": "1.0",
  "versionHeightOffset": -1,
  "pathFilters": ["."]
}
```

This is an ADR-level decision; flag it for review before merging.

### Adding a package

1. Scaffold the csproj under `src/`. Set required metadata:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <KoanPackageKind>Periphery</KoanPackageKind>    <!-- required for metadata audit -->
    <TargetFramework>net10.0</TargetFramework>
    <Description>One sentence describing what this package does.</Description>
    <PackageTags>$(CommonPackageTags);your;specific;tags</PackageTags>
  </PropertyGroup>
</Project>
```

2. Create the package's `version.json` by running the baseline script:

```pwsh
pwsh scripts/versioning/Initialize-NbgvBaseline.ps1 -WhatIf   # preview
pwsh scripts/versioning/Initialize-NbgvBaseline.ps1            # write
```

Or create it manually:

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/Nerdbank.GitVersioning/main/src/NerdBank.GitVersioning/version.schema.json",
  "version": "0.17",
  "versionHeightOffset": -1,
  "pathFilters": ["."]
}
```

3. Verify the metadata is complete:

```pwsh
pwsh scripts/versioning/Audit-NuGetMetadata.ps1
# Should report 0 missing Description / PackageTags / KoanPackageKind.
```

4. On the next merge to `main`, the package is packed and published automatically.

### Mark a package non-packable

Temporarily exclude a package from NuGet publishing:

```xml
<!-- Inside the csproj's first <PropertyGroup>: -->
<IsPackable>false</IsPackable>
```

Effect:
- The workflow packs the csproj but produces no `.nupkg` (MSBuild no-ops the pack).
- No NuGet push attempt for this package.
- The csproj still builds, so dependents still work in-repo.

To re-enable: delete the `IsPackable` line.

### Preview version

```pwsh
# Single package:
dotnet nbgv get-version -p src/Koan.Core

# All packages (slow -- probes each folder):
Get-ChildItem -Path src -Filter version.json -Recurse | ForEach-Object {
  $pkg = Split-Path (Split-Path $_.FullName -Parent) -Leaf
  $ver = dotnet nbgv get-version -p (Split-Path $_.FullName -Parent) --format json | ConvertFrom-Json
  [pscustomobject]@{ Package = $pkg; Version = $ver.NuGetPackageVersion }
} | Format-Table -AutoSize
```

---

## Failure -> recovery

### Symptom: package version has a prerelease suffix on main

**Why it happens:** the pack step ran without `-p:PublicRelease=true`.

**Recovery:** re-run the workflow or add `-p:PublicRelease=true` to any manual pack command.

### Symptom: wrong patch number (expected 5, got 12)

**Why it happens:** nbgv counts every commit in the folder's history since `version.json` last changed, including merge commits and commits from other branches that touched the folder.

**Recovery:**

```pwsh
# See which commits nbgv is counting.
dotnet nbgv get-version -p src/Koan.YourPackage --format json | ConvertFrom-Json | Select-Object -ExpandProperty GitCommitIdShort
# Then inspect the log:
git log --oneline -- src/Koan.YourPackage/
```

If the count is genuinely wrong (e.g., a mass-refactor touched the folder incidentally), the only way to reset patch to 0 is to bump the `version` field in the package's `version.json`.

### Symptom: new package version is not on nuget.org after a green workflow run

**Why it happens:** the package's version on nuget.org already matched the computed version (a previous publish or a duplicate run), so `--skip-duplicate` silently skipped it.

**Recovery:**

```pwsh
# Check what version the workflow produced.
dotnet nbgv get-version -p src/Koan.YourPackage --format json | ConvertFrom-Json | Select-Object NuGetPackageVersion

# Check what's on nuget.org (lowercased package id).
curl -s https://api.nuget.org/v3-flatcontainer/sylin.koan.yourpackage/index.json
```

If those match, the package IS published -- it just didn't increment because no commits touched the folder. To force a new publish, add any commit to the package folder and merge again.

### Symptom: new package doesn't show up in the pack output at all

**Why it happens:** the csproj has `IsPackable=false` (directly or via an ancestor `Directory.Build.props`), or no `version.json` exists in the package folder.

**Recovery:**

```pwsh
# Check the audit.
pwsh scripts/versioning/Audit-NuGetMetadata.ps1

# Probe-pack locally.
dotnet pack src/Koan.NewThing/Koan.NewThing.csproj -c Release "-p:PublicRelease=true" -o artifacts/probe
```

If `IsPackable=false` is inherited from a parent folder (e.g., `src/Services/`), override it in the csproj:

```xml
<IsPackable>true</IsPackable>
```

---

## Anti-patterns

- **Don't add `<Version>` directly to a csproj.** The version is resolved by nbgv via `Directory.Build.targets`. A direct `<Version>` overrides nbgv and creates drift.
- **Don't hand-edit `version.json` to set a specific patch.** The patch is computed from git height. Set the `version` field (major.minor) only; let nbgv own the patch.
- **Don't bump major.minor without an ADR for packages that other packages compile against.** A major.minor change is a contract claim. For shared abstractions, flag it for review.
- **Don't delete a package's `version.json`.** The package would fall back to the root version.json, picking up a version based on repo-wide git height rather than its own folder history.
- **Don't use `versionHeightOffset` other than `-1`.** The current setting ensures the commit introducing `version.json` lands on `major.minor.0`. Other values produce unexpected patch numbers.

---

## References

- [ARCH-0085 -- Versioning compatibility and automation](../decisions/ARCH-0085-versioning-compatibility-and-automation.md)
- [ARCH-0082 -- Per-package versioning strategy](../decisions/ARCH-0082-versioning-strategy.md)
- [ARCH-0083 -- Operational workbooks](../decisions/ARCH-0083-operational-workbooks.md)
- [scripts/versioning/Initialize-NbgvBaseline.ps1](../../scripts/versioning/Initialize-NbgvBaseline.ps1) -- per-package version.json scaffolding
- [scripts/versioning/Audit-NuGetMetadata.ps1](../../scripts/versioning/Audit-NuGetMetadata.ps1) -- metadata diagnostic
- [nuget-publishing.md](nuget-publishing.md) -- what happens after versions are decided
