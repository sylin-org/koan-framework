# NuGet publishing

How Koan packages reach nuget.org. The `release-on-main` workflow handles this on every merge to `main`. This workbook is for understanding what happens, verifying it worked, and recovering when it doesn't.

> Companion workbook: [versioning.md](versioning.md) — how versions are decided before publishing.
> Companion ADR: [ARCH-0082](../decisions/ARCH-0082-versioning-strategy.md) — why the system is shaped this way.

---

## When to use this

- A merge to `main` is queued and you want to know what the workflow will do
- A release just ran and you want to verify it succeeded
- The workflow failed and you need to recover
- You're adding a new package and want to confirm it'll publish
- You need to release manually (CI is offline, or you're on a fork)

**Prerequisites:**

- Repository secret `NUGET_API_KEY` must be set in GitHub: Settings → Secrets and variables → Actions → Secrets
- Branch protection on `main` must allow `github-actions[bot]` to push (or be permissive enough that the workflow's commit lands)
- Versions decided via [versioning.md](versioning.md) — this workbook picks up after that

---

## Mental model (30 seconds)

Packages flow to nuget.org through one workflow: [.github/workflows/release-on-main.yml](../../.github/workflows/release-on-main.yml). It runs automatically on every push to `main`.

What it does, in order:

1. **Builds** the solution in Release config. If the build fails, the workflow stops here — no broken main gets a release tag.
2. **Computes new versions** via `Update-Versions.ps1 -AutoBumpKernel`. This writes `build/versions.props` and `artifacts/bumped-packages.txt` (the exact csprojs to publish).
3. **Commits + tags + pushes**. The `chore(release): bump versions` commit lands on `main` and `release/v<kernel-version>` tags the same commit.
4. **Packs ONLY the bumped packages.** No point packing 95 packages that nuget.org would reject as duplicates. The manifest from step 2 says exactly what to pack.
5. **Publishes to nuget.org.** Both the `.nupkg` (main package) and `.snupkg` (symbols) are pushed. `--skip-duplicate` keeps re-runs safe.

A "no-op release" is normal: if nothing in `src/` changed (docs-only or test-only merge), the workflow detects no bumps, skips the commit/tag/publish, and reports "Nothing to release" in its summary.

Package IDs publish as `Sylin.Koan.<name>` (the `Sylin.` prefix is set in the root [Directory.Build.props](../../Directory.Build.props); the code namespaces stay `Koan.*`).

---

## Happy path

You don't run anything to publish. The workflow does it on every merge to `main`. Your job:

```pwsh
# 1. Land your work via PR to main. (Or push directly if you're solo.)
# 2. Watch the workflow run.
```

GitHub → **Actions** tab → **Release on main** → most recent run. Typical timing:

| Stage | Duration |
|---|---|
| Checkout + setup .NET | ~1 min |
| Build Release (full solution) | 2-4 min |
| Compute versions | ~10s |
| Commit + tag + push | ~10s |
| Restore + pack the bumped csprojs | 4-8 min (depends on how many bumped) |
| Push to nuget.org | 3-6 min (one push per package, network-bound) |
| **Total** | **~10-15 min** for a mass-bump, ~3-5 min for a one-package hotfix |

**Success looks like:**

- Green checkmark on the workflow run
- A new `release/v<version>` tag on origin
- A `chore(release): bump versions [skip release]` commit on `main` authored by `github-actions[bot]`
- New package versions visible at https://www.nuget.org/packages?q=Sylin.Koan
- Run summary shows kernel version + "Tag created"

If you see all of those, you're done.

---

## Scenarios

| If you want to... | Go to |
|---|---|
| Verify a release actually published | [Verify a release](#verify-a-release) |
| Set up NUGET_API_KEY for the first time | [First-time secret setup](#first-time-secret-setup) |
| Skip publishing for a specific merge | [Skip a release](#skip-a-release) |
| Re-publish after a network blip | [Re-publish without bumping](#re-publish-without-bumping) |
| Publish manually (CI is unavailable) | [Manual publish](#manual-publish) |

### Verify a release

After a merge to `main` whose workflow finished green:

```pwsh
# Pull the new tag + commit.
git fetch origin --tags --prune
git switch main && git pull origin main

# What tag was created?
git tag --list 'release/v*' | sort -V | tail -3
#    The newest should be the version the workflow announced.

# What's on main now?
git log --oneline -3 origin/main
#    Top commit should be: chore(release): bump versions [skip release] by github-actions[bot]
#    Second commit should be: your PR merge.

# What versions did packages get?
git show origin/main:build/versions.props | head -20
#    KoanKernelVersion + per-package entries should reflect the new release.

# What packages went up?
# Visit: https://www.nuget.org/packages?q=Sylin.Koan
# Or: dotnet package search Sylin.Koan --prerelease
```

### First-time secret setup

If `NUGET_API_KEY` isn't set in the repo, the workflow tags + pushes versions.props but skips the NuGet push with a `::warning::`. To enable publishing:

1. Get an API key from https://www.nuget.org/account/apikeys (scope: push to `Sylin.*`)
2. GitHub → Repo Settings → Secrets and variables → Actions → **Secrets** tab → New repository secret
   - Name: `NUGET_API_KEY`
   - Value: paste the key
3. Next merge to `main` picks it up automatically. No workflow change needed.

### Skip a release

Sometimes you want a merge to land without publishing — e.g., you're testing the workflow itself, or you want to batch several merges into one release.

Include `[skip release]` in the merge commit subject:

```
chore: prepping for tomorrow's batch release [skip release]
```

The workflow's job-level conditional `if: "!contains(github.event.head_commit.message, '[skip release]')"` short-circuits the entire job. No build, no version compute, no tag, no publish.

### Re-publish without bumping

The workflow already tagged and published, but a few packages failed to push (transient nuget.org error, network hiccup). You want to retry just the publish.

Two options:

**Option 1: Re-run the failed workflow job.** GitHub → Actions → the run → "Re-run failed jobs". This re-attempts the publish step. Idempotent because `--skip-duplicate` means already-published packages are skipped silently.

**Option 2: Push manually from local.**

```pwsh
# Rebuild locally with the current versions.props.
git switch main && git pull origin main
dotnet build Koan.sln -c Release "-p:NuGetAudit=false"

# Pack only the packages that should be at the current kernel version.
pwsh scripts/versioning/Update-Versions.ps1 -AutoBumpKernel
#    This regenerates artifacts/bumped-packages.txt for the current state.
#    If versions.props is already up to date, this is a no-op for the file.

# Pack each csproj in the manifest.
$csprojs = Get-Content artifacts/bumped-packages.txt | Where-Object { $_ }
New-Item -ItemType Directory -Path artifacts/nuget -Force | Out-Null
foreach ($p in $csprojs) {
  dotnet pack $p -c Release "-p:NuGetAudit=false" -p:PackageOutputPath=(Resolve-Path artifacts/nuget)
}

# Push.
$env:NUGET_API_KEY = "<your-key>"
foreach ($f in Get-ChildItem artifacts/nuget/*.nupkg) {
  dotnet nuget push $f.FullName --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
}
foreach ($f in Get-ChildItem artifacts/nuget/*.snupkg) {
  dotnet nuget push $f.FullName --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
}
```

### Manual publish

Use this when CI is unavailable (offline, on a fork, or you're debugging the workflow itself).

Full local flow:

```pwsh
# 1. Ensure main is current and tests pass.
git switch main && git pull origin main
dotnet build Koan.sln -c Release "-p:NuGetAudit=false"

# 2. Compute new versions (mirrors what the workflow does).
pwsh scripts/versioning/Update-Versions.ps1 -AutoBumpKernel
git diff build/versions.props        # review
cat artifacts/bumped-packages.txt    # see what'll pack

# 3. Commit + tag locally.
git add build/versions.props
git commit -m "chore(release): bump versions"
$version = (Select-Xml -Path build/versions.props -XPath '//KoanKernelVersion' | Select-Object -ExpandProperty Node).InnerText.Trim()
pwsh scripts/versioning/New-Release.ps1 -Version $version -Push

# 4. Pack + push (same as "Re-publish without bumping" above).
```

---

## Failure → recovery

### Symptom: workflow status "In progress" for over 20 minutes

**Why it happens:** rare — usually a `dotnet nuget push` hanging on a flaky network connection. Less rare: nuget.org throttling on a mass-bump.

**Recovery:**

```text
GitHub → Actions → the running job → "..." menu → Cancel workflow
```

Then re-run from the workflow's UI. The second run will skip already-published packages via `--skip-duplicate`.

### Symptom: workflow failed with "Tag release/v0.X.0 already exists. Skipping..."

**Why it happens:** the workflow tag-check refuses to overwrite an existing tag. This usually means a prior run already created the tag (possibly partially — e.g., tagged but didn't publish).

**Recovery:**

```pwsh
# 1. Confirm the tag actually exists on origin.
git fetch origin --tags --prune
git ls-remote --tags origin "refs/tags/release/v0.X.0"

# 2. Check whether the tag's commit is on main.
git fetch origin main
git merge-base --is-ancestor release/v0.X.0 origin/main
#   If exit 0: the release commit IS on main. The publish was probably partial — see next symptom.
#   If exit non-zero: the tag points to a commit not on main (the workflow tagged but couldn't push main).

# Case A — tag's commit is on main but publish was partial:
#   Re-run the failed publish step from the GitHub Actions UI.

# Case B — tag's commit is NOT on main (dangling tag):
git push origin --delete release/v0.X.0
git tag -d release/v0.X.0
#   Then fix the underlying push failure (see "Symptom: 'git push origin main' rejected" below)
#   and merge anything to main to re-trigger the workflow.
```

### Symptom: `git push origin main` rejected (branch protection)

**Why it happens:** branch protection on `main` requires PRs for all changes; the workflow's direct push from `github-actions[bot]` is treated as a non-PR change.

You'll see this in the workflow log around the "Commit and push version bump" step:

```
remote: error: GH013: Repository rule violations found...
remote: - Changes must be made through a pull request.
```

**Recovery:**

```text
GitHub → Repo Settings → Branches (classic) or Rules → Rulesets (modern)
```

For classic branch protection:
- Edit the `main` rule
- Either: check "Allow specified actors to bypass required pull requests" and add `github-actions[bot]` (best — preserves protection for humans)
- Or: uncheck "Require a pull request before merging" (simpler — disables PR enforcement; only fine for solo projects)
- Save

For modern Rulesets:
- Edit the ruleset
- Add `github-actions` to the Bypass list (Repository admin → Add bypass)
- Save

After saving, re-trigger the workflow by merging anything to `main`. The workflow's commit will land cleanly.

### Symptom: pack step failed with NU5017 or NU5019 on an analyzer/generator package

**Why it happens:** Roslyn analyzer and source-generator projects produce no normal compile output — the `.dll` ships under `analyzers/dotnet/cs/` instead of `lib/`. `dotnet pack` tries to also create a `.snupkg` (symbols package) and fails because there's no source code to symbolize.

The `.nupkg` is usually created successfully BEFORE the error; the workflow now tolerates this. But if you're adding a NEW analyzer/generator, you need the right csproj settings:

**Recovery (csproj fix):**

```xml
<!-- Inside the analyzer's csproj <PropertyGroup>: -->
<IsRoslynComponent>true</IsRoslynComponent>
<IncludeSymbols>false</IncludeSymbols>
<IncludeSource>false</IncludeSource>
<NoWarn>$(NoWarn);NU5017;NU5019;NU5128</NoWarn>
```

Then pack locally to verify:

```pwsh
dotnet pack src/Koan.MyAnalyzer/Koan.MyAnalyzer.csproj -c Release -o /tmp/test
ls /tmp/test/*.nupkg     # should show one nupkg, no snupkg
```

Templates: [Koan.Cache.Analyzers.csproj](../../src/Koan.Cache.Analyzers/Koan.Cache.Analyzers.csproj), [Koan.Core.Registry.Generators.csproj](../../src/Koan.Core.Registry.Generators/Koan.Core.Registry.Generators.csproj).

### Symptom: pack step failed with NETSDK1004 "Assets file not found"

**Why it happens:** the csproj wasn't restored before pack. Usually means the csproj isn't in `Koan.sln`, so the upfront `dotnet build Koan.sln` didn't restore its dependencies.

**Recovery:** the workflow already runs `dotnet restore <csproj>` before each pack since the hardening commit. If you see this error in a workflow log, the workflow is older than that hardening. Update the workflow file from `main`.

If it happens locally during manual pack, add the explicit restore:

```pwsh
dotnet restore src/Path/To/MyPackage.csproj "-p:NuGetAudit=false"
dotnet pack src/Path/To/MyPackage.csproj -c Release "-p:NuGetAudit=false" -o ./artifacts/nuget
```

### Symptom: package published with wrong ID (`Koan.X` instead of `Sylin.Koan.X`)

**Why it happens:** the `Sylin.` prefix comes from [Directory.Build.props](../../Directory.Build.props):

```xml
<PackageId Condition="'$(IsPackable)' != 'false' and '$(PackageId)' == ''">Sylin.$(MSBuildProjectName)</PackageId>
```

For this to apply, the csproj must:
1. Not be in a non-packable subtree (`samples/`, `src/Services/`, `tests/`)
2. Not set its own `<PackageId>` explicitly
3. Be a descendant of the root Directory.Build.props (or an intermediate one that imports it)

**Recovery:**

```pwsh
# Verify which Directory.Build.props files apply to the csproj.
# Walk up from src/Foo/Foo.csproj, listing every Directory.Build.props you find.
# Each parent should either set IsPackable=false (excluding the csproj) or import the root one.

# Check the pack output for the actual PackageId.
dotnet pack src/Foo/Foo.csproj -c Release -o /tmp/test --nologo
unzip -p /tmp/test/*.nupkg '*.nuspec' | grep -E '<id>|<version>'
```

If the prefix is missing, check that the intermediate Directory.Build.props (e.g., `src/Connectors/Directory.Build.props`) explicitly imports the root one:

```xml
<Project>
  <Import Project="..\..\Directory.Build.props" />   <!-- correct relative path -->
  ...
</Project>
```

### Symptom: package published but missing description, tags, or other metadata

**Why it happens:** the csproj is missing `<Description>` or `<PackageTags>`. NuGet accepts these but the package's listing page looks bare.

**Recovery:**

```pwsh
# Audit every packable csproj for required metadata.
pwsh scripts/versioning/Audit-NuGetMetadata.ps1
#   Should report 0 missing Description / PackageTags / KoanPackageKind.
#   If any are missing, fix them in the csprojs.

# Then republish those packages (see "Re-publish without bumping" scenario).
```

Add to the csproj's first `<PropertyGroup>`:

```xml
<Description>One sentence describing what this package does for consumers.</Description>
<PackageTags>$(CommonPackageTags);your;specific;tags</PackageTags>
```

### Symptom: workflow says "Nothing to release" but you expected packages

**Why it happens:** `Update-Versions.ps1 -AutoBumpKernel` saw no commits in any package folder since the last release tag. Either the commits were docs-only, test-only, or didn't touch `src/` at all.

**Recovery:** see the matching symptom in [versioning.md → Failure → recovery](versioning.md#symptom-show-versionstatusps1-says-no-bumps-but-you-expected-one).

---

## Anti-patterns

- **Don't disable `--skip-duplicate` on `dotnet nuget push`.** It's what makes re-runs safe. Without it, re-running the publish step after a partial failure errors on every already-published package.
- **Don't add packages to nuget.org that aren't framework libraries.** Sample apps, internal services, and integration test fixtures all have `<IsPackable>false</IsPackable>` set (directly or inherited). Don't override unless you mean it.
- **Don't store `NUGET_API_KEY` in a workflow file, csproj, or any committed file.** It's a secret. It lives in repo Secrets only.
- **Don't push tags by hand to trigger a release.** The workflow drives the tagging. Manually pushing a `release/v*` tag while the workflow expects to create it produces the "Tag already exists" failure mode.
- **Don't `git push --force origin main`.** Even when you're recovering from a broken release. The workflow's commits are signed by `github-actions[bot]` and have value as audit trail. If you need to undo a release, delete the tag and the workflow will recreate cleanly.

---

## References

- [ARCH-0082 — Two-tier versioning](../decisions/ARCH-0082-versioning-strategy.md) — why packages are versioned independently
- [ARCH-0083 — Operational workbooks](../decisions/ARCH-0083-operational-workbooks.md) — the standard this workbook follows
- [.github/workflows/release-on-main.yml](../../.github/workflows/release-on-main.yml) — the workflow this workbook describes
- [scripts/versioning/Update-Versions.ps1](../../scripts/versioning/Update-Versions.ps1) — version computation + manifest emission
- [scripts/versioning/New-Release.ps1](../../scripts/versioning/New-Release.ps1) — manual tag + push tool
- [scripts/versioning/Audit-NuGetMetadata.ps1](../../scripts/versioning/Audit-NuGetMetadata.ps1) — metadata audit
- [Directory.Build.props](../../Directory.Build.props) — package ID prefix, common metadata
- [versioning.md](versioning.md) — what determines which packages bump
