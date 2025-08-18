## Release flow: bump, merge, tag, publish

Use the helper script to bump version, fast-forward `main` from `dev`, tag `vX.Y.Z`, and push. The tag triggers the NuGet publish workflow.

### Prerequisites
- Clean working tree (no uncommitted changes).
- `dev` is up to date and contains the release commit(s).
- `main` can fast-forward to `dev` (no extra commits on `main`).
- Org/repo secret `NUGET_API_KEY` is set for nuget.org publishing.

### Run the script (PowerShell)

```powershell
# From repo root
# Bump Minor (auto +1), merge dev -> main (ff-only), tag vX.Y.Z, push
pwsh -File ./scripts/versioning/release-from-dev.ps1 -Part Minor

# Other options
pwsh -File ./scripts/versioning/release-from-dev.ps1 -Part Major
pwsh -File ./scripts/versioning/release-from-dev.ps1 -Part Patch

# Dry run (no pushes/tags)
pwsh -File ./scripts/versioning/release-from-dev.ps1 -Part Minor -DryRun

# PR-only mode (when main is protected and requires PRs)
# Bumps version on dev and pushes; stops before touching main. Optionally opens a PR if GitHub CLI is installed.
pwsh -File ./scripts/versioning/release-from-dev.ps1 -Part Minor -PrOnly -CreatePr
```

What it does
1) Updates `version.json` by incrementing Major/Minor/Patch.
2) Commits and pushes to `dev`.
3) Checks out `main` and fast-forward merges from `dev`.
4) Creates an annotated tag `vX.Y.Z` and pushes `main` and the tag.
5) The tag triggers `.github/workflows/nuget-release.yml` which packs and publishes to nuget.org.

PR-only variant (-PrOnly)
1) Updates `version.json` and commits to `dev`, pushes `dev`.
2) Does not checkout or push `main`, and does not create a tag.
3) If `-CreatePr` is specified and GitHub CLI (`gh`) is available, it opens a PR: base `main`, head `dev`.
4) After the PR merges (fast-forward), create the annotated tag `vX.Y.Z` on `main` and push the tag to trigger the publish workflow.

### Troubleshooting
- Fast-forward merge failed: `main` has commits not on `dev`. Re-align by reverting the extra commits or merge `main` into `dev`, then try again.
- Working tree not clean: commit or stash changes before running.
- Protected branch: if `main` blocks direct pushes, use the PR-only flow (`-PrOnly`) and tag after merge.

### Related
- Local pack/push (no tagging): `./scripts/pack-and-push.ps1`
- Publishing docs: `docs/support/nuget-publish.md`
