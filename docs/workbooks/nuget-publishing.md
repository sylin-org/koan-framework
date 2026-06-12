# NuGet publishing

How Koan packages reach nuget.org. The `release-on-main` workflow handles this on every merge to `main`. This workbook is for understanding what happens, verifying it worked, and recovering when it doesn't.

> Companion workbook: [versioning.md](versioning.md) -- how per-package versions are computed.
> Companion ADRs: [ARCH-0082](../decisions/ARCH-0082-versioning-strategy.md), [ARCH-0085](../decisions/ARCH-0085-versioning-compatibility-and-automation.md).

---

## When to use this

- A merge to `main` is queued and you want to know what the workflow will do
- A release just ran and you want to verify it succeeded
- The workflow failed and you need to recover
- You're adding a new package and want to confirm it'll publish
- You need to release manually (CI is offline, or you're on a fork)

**Prerequisites:**

- Repository secret `NUGET_API_KEY` must be set in GitHub: Settings -> Secrets and variables -> Actions -> Secrets
- Branch protection on `main` must allow tags to be pushed by `github-actions[bot]`
- Per-package `version.json` files present (run `Initialize-NbgvBaseline.ps1` for new packages)

---

## Mental model (30 seconds)

Packages flow to nuget.org through one workflow: [.github/workflows/release-on-main.yml](../../.github/workflows/release-on-main.yml). It runs automatically on every push to `main`.

What it does, in order:

1. **Builds** the solution in Release config with `PublicRelease=true`. If the build fails, the workflow stops here -- no broken main gets a release tag.
2. **Packs every packable csproj** under `src/`. Nerdbank.GitVersioning stamps each package with its own version derived from the git history of that package's folder. `--skip-duplicate` makes all pushes idempotent: packages whose version did not change since the last publish are silently skipped by nuget.org.
3. **Creates a date-based release tag** (`release/YYYY-MM-DD`, or `release/YYYY-MM-DD-2` if the same day has already been tagged).
4. **Publishes to nuget.org.** Both the `.nupkg` and `.snupkg` (symbols) are pushed.

There is no "nothing to release" short-circuit: every merge to `main` packs and attempts to push. Packages whose version is unchanged are simply skipped by nuget.org via `--skip-duplicate`.

Package IDs publish as `Sylin.Koan.<name>` (the `Sylin.` prefix is set in the root [Directory.Build.props](../../Directory.Build.props); code namespaces stay `Koan.*`).

---

## Happy path

You don't run anything to publish. The workflow does it on every merge to `main`. Your job:

```pwsh
# 1. Land your work via PR to main.
# 2. Watch the workflow run.
```

GitHub -> **Actions** tab -> **Release on main** -> most recent run. Typical timing:

| Stage | Duration |
|---|---|
| Checkout + setup .NET | ~1 min |
| Build Release (full solution) | 2-4 min |
| Pack all packable csprojs | 4-10 min (depends on project count) |
| Compute tag + push | ~10s |
| Push to nuget.org | 3-6 min (one push per package, network-bound) |
| **Total** | **~10-20 min** |

**Success looks like:**

- Green checkmark on the workflow run
- A new `release/YYYY-MM-DD` tag on origin
- New or unchanged package versions visible at https://www.nuget.org/packages?q=Sylin.Koan
- Run summary shows the release tag and package count

If you see all of those, you're done.

---

## Scenarios

| If you want to... | Go to |
|---|---|
| Verify a release actually published | [Verify a release](#verify-a-release) |
| Set up NUGET_API_KEY for the first time | [First-time secret setup](#first-time-secret-setup) |
| Skip publishing for a specific merge | [Skip a release](#skip-a-release) |
| Re-publish after a network blip | [Re-publish without re-tagging](#re-publish-without-re-tagging) |
| Publish manually (CI is unavailable) | [Manual publish](#manual-publish) |

### Verify a release

After a merge to `main` whose workflow finished green:

```pwsh
# Pull the new tag.
git fetch origin --tags --prune

# What tag was created?
git tag --list 'release/*' | sort | tail -3

# What packages went up?
# Visit: https://www.nuget.org/packages?q=Sylin.Koan
# Or spot-check a specific package:
curl -s https://api.nuget.org/v3-flatcontainer/sylin.koan.core/index.json
```

### First-time secret setup

If `NUGET_API_KEY` is not set, the workflow creates the tag but skips the NuGet push with a `::warning::`. To enable publishing:

1. Get an API key from https://www.nuget.org/account/apikeys (scope: push to `Sylin.*`)
2. GitHub -> Repo Settings -> Secrets and variables -> Actions -> **Secrets** tab -> New repository secret
   - Name: `NUGET_API_KEY`
   - Value: paste the key
3. Next merge to `main` picks it up automatically. No workflow change needed.

### Skip a release

Sometimes you want a merge to land without publishing -- e.g., you're testing the workflow itself, or you want to batch several merges into one release.

Include `[skip release]` in the merge commit subject:

```
chore: prepping for tomorrow's batch release [skip release]
```

The workflow's job-level conditional `if: "!contains(github.event.head_commit.message, '[skip release]')"` short-circuits the entire job. No build, no pack, no tag, no publish.

### Re-publish without re-tagging

The workflow tagged and packed, but a few packages failed to push (transient nuget.org error, network hiccup). You want to retry just the publish.

**Option 1: Re-run the failed workflow job.** GitHub -> Actions -> the run -> "Re-run failed jobs". The pack step re-runs from scratch and re-pushes; `--skip-duplicate` skips already-published packages.

**Option 2: Push manually from local.**

```pwsh
git switch main && git pull origin main
dotnet build Koan.sln -c Release "-p:NuGetAudit=false" "-p:PublicRelease=true"

New-Item -ItemType Directory -Path artifacts/nuget -Force | Out-Null
Get-ChildItem -Path src -Recurse -Filter *.csproj | ForEach-Object {
  dotnet pack $_.FullName -c Release --nologo "-p:PublicRelease=true" "-p:NuGetAudit=false" -o artifacts/nuget
}

$env:NUGET_API_KEY = "<your-key>"
Get-ChildItem artifacts/nuget/*.nupkg | ForEach-Object {
  dotnet nuget push $_.FullName --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
}
Get-ChildItem artifacts/nuget/*.snupkg -ErrorAction SilentlyContinue | ForEach-Object {
  dotnet nuget push $_.FullName --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
}
```

### Manual publish

Use this when CI is unavailable (offline, on a fork, debugging the workflow itself). This is the same as "Re-publish without re-tagging" above, plus tagging:

```pwsh
# 1. Ensure main is current and build is clean.
git switch main && git pull origin main
dotnet build Koan.sln -c Release "-p:NuGetAudit=false" "-p:PublicRelease=true"

# 2. Pack all packable csprojs.
New-Item -ItemType Directory -Path artifacts/nuget -Force | Out-Null
Get-ChildItem -Path src -Recurse -Filter *.csproj | ForEach-Object {
  dotnet pack $_.FullName -c Release --nologo "-p:PublicRelease=true" "-p:NuGetAudit=false" -o artifacts/nuget
}

# 3. Review what was produced.
Get-ChildItem artifacts/nuget/*.nupkg | Select-Object Name

# 4. Create and push a date-based tag.
$tag = "release/$(Get-Date -Format 'yyyy-MM-dd')"
git tag -a $tag -m "Release $tag"
git push origin $tag

# 5. Publish (see step above for the push loop).
```

---

## Failure -> recovery

### Symptom: workflow status "In progress" for over 20 minutes

**Why it happens:** usually a `dotnet nuget push` hanging on a flaky network connection, or nuget.org throttling on a large pack.

**Recovery:**

```text
GitHub -> Actions -> the running job -> "..." menu -> Cancel workflow
```

Then re-run from the workflow's UI. The second run re-packs (fast, incremental) and skips already-published packages via `--skip-duplicate`.

### Symptom: workflow failed at pack with zero nupkg produced

**Why it happens:** all csprojs under `src/` have `IsPackable=false` (or are in a non-packable subtree), or every individual pack call failed.

**Recovery:**

```pwsh
# Audit packable csprojs and their metadata.
pwsh scripts/versioning/Audit-NuGetMetadata.ps1

# Probe-pack a single csproj locally.
dotnet pack src/Koan.Core/Koan.Core.csproj -c Release "-p:PublicRelease=true" -o artifacts/probe
Get-ChildItem artifacts/probe/*.nupkg | Select-Object Name
```

Check that the csproj is in a packable subtree and has a `version.json` in its folder (see [versioning.md](versioning.md)).

### Symptom: pack step failed with NU5017 or NU5019 on an analyzer/generator package

**Why it happens:** Roslyn analyzer and source-generator projects produce no normal compile output. `dotnet pack` tries to create a `.snupkg` and fails because there is no source to symbolize.

The `.nupkg` is usually created successfully before the error; the workflow warns and continues. But if you are adding a new analyzer/generator, add the right csproj settings:

```xml
<IsRoslynComponent>true</IsRoslynComponent>
<IncludeSymbols>false</IncludeSymbols>
<IncludeSource>false</IncludeSource>
<NoWarn>$(NoWarn);NU5017;NU5019;NU5128</NoWarn>
```

Templates: [Koan.Cache.Analyzers.csproj](../../src/Koan.Cache.Analyzers/Koan.Cache.Analyzers.csproj), [Koan.Core.Registry.Generators.csproj](../../src/Koan.Core.Registry.Generators/Koan.Core.Registry.Generators.csproj).

### Symptom: package published with wrong version (prerelease suffix, or wrong major.minor)

**Why it happens:** the pack ran without `-p:PublicRelease=true` (prerelease suffix), or the package's `version.json` has the wrong `version` field.

**Recovery:**

```pwsh
# Check what version nbgv would compute for a package.
dotnet nbgv get-version -p src/Koan.Core
# With PublicRelease=true:
dotnet nbgv get-version -p src/Koan.Core --format json | ConvertFrom-Json | Select-Object NuGetPackageVersion
```

See [versioning.md](versioning.md) for how to correct the `version.json`.

### Symptom: package published with wrong ID (`Koan.X` instead of `Sylin.Koan.X`)

**Why it happens:** the `Sylin.` prefix comes from [Directory.Build.props](../../Directory.Build.props):

```xml
<PackageId Condition="'$(IsPackable)' != 'false' and '$(PackageId)' == ''">Sylin.$(MSBuildProjectName)</PackageId>
```

For this to apply, the csproj must not set its own `<PackageId>` and must be a descendant of the root `Directory.Build.props`.

**Recovery:**

```pwsh
# Verify the actual PackageId in the produced nupkg.
dotnet pack src/Foo/Foo.csproj -c Release -o /tmp/test --nologo
# On Linux/Mac:
unzip -p /tmp/test/*.nupkg '*.nuspec' | grep -E '<id>|<version>'
# On Windows:
Expand-Archive /tmp/test/*.nupkg /tmp/probe -Force
Get-Content /tmp/probe/*.nuspec | Select-String '<id>|<version>'
```

### Symptom: package published but missing description, tags, or other metadata

**Why it happens:** the csproj is missing `<Description>` or `<PackageTags>`.

**Recovery:**

```pwsh
# Audit every packable csproj for required metadata.
pwsh scripts/versioning/Audit-NuGetMetadata.ps1
# Reports missing Description / PackageTags / KoanPackageKind.
```

Add to the csproj's first `<PropertyGroup>`:

```xml
<Description>One sentence describing what this package does for consumers.</Description>
<PackageTags>$(CommonPackageTags);your;specific;tags</PackageTags>
```

---

## Anti-patterns

- **Don't disable `--skip-duplicate` on `dotnet nuget push`.** It makes re-runs safe. Without it, retrying after a partial failure errors on every already-published package.
- **Don't add packages to nuget.org that aren't framework libraries.** Sample apps, internal services, and integration test fixtures all have `<IsPackable>false</IsPackable>` set. Don't override unless you mean it.
- **Don't store `NUGET_API_KEY` in a workflow file, csproj, or any committed file.** It is a secret. It lives in repo Secrets only.
- **Don't push tags by hand to trigger a release.** The workflow creates the tag. Pushing a `release/*` tag manually has no effect (the trigger is `push to main`, not a tag push).
- **Don't `git push --force origin main`.** Even when recovering from a broken release. The tag is the audit trail; delete and re-tag cleanly if you need to redo a release.

---

## References

- [ARCH-0082 -- Per-package versioning](../decisions/ARCH-0082-versioning-strategy.md)
- [ARCH-0085 -- Versioning compatibility and automation](../decisions/ARCH-0085-versioning-compatibility-and-automation.md)
- [ARCH-0083 -- Operational workbooks](../decisions/ARCH-0083-operational-workbooks.md)
- [.github/workflows/release-on-main.yml](../../.github/workflows/release-on-main.yml) -- the workflow this workbook describes
- [scripts/versioning/Audit-NuGetMetadata.ps1](../../scripts/versioning/Audit-NuGetMetadata.ps1) -- metadata diagnostic
- [scripts/versioning/Initialize-NbgvBaseline.ps1](../../scripts/versioning/Initialize-NbgvBaseline.ps1) -- per-package version.json scaffolding
- [Directory.Build.props](../../Directory.Build.props) -- package ID prefix, common metadata
- [versioning.md](versioning.md) -- how per-package versions are computed
