## Release flow: PR to main, auto-tag, publish

Releases are driven by `version.json`. After a PR merges to `main`, GitHub Actions reads the version and creates the tag `vX.Y.Z` automatically. The tag triggers the NuGet publish workflow.

### Prerequisites
- Clean working tree (no uncommitted changes).
- `dev` is up to date and contains the release commit(s).
- `main` can fast-forward to `dev` (no extra commits on `main`).
- Org/repo secret `NUGET_API_KEY` is set for nuget.org publishing.

### How to release

1) On `dev`, update `version.json` with the desired new version (semantic version).
2) Open a PR from `dev` to `main` and merge (fast-forward preferred).
3) The `tag-on-main` workflow will create an annotated tag `vX.Y.Z` on `main` if it does not already exist.
4) The tag triggers `.github/workflows/nuget-release.yml` which packs and publishes to nuget.org.

What happens
1) You update `version.json` on `dev` in a PR.
2) After merge to `main`, `tag-on-main` creates `vX.Y.Z` on the merge commit.
3) `nuget-release` (triggered by the tag) packs all libraries and meta packages and pushes to nuget.org.

Notes
- Do not manually create tags in normal flow; let CI tag `main` from `version.json`.
- If the tag already exists, the workflow skips tagging.
- Ensure `NUGET_API_KEY` is configured for the repo/org.

### Troubleshooting
- Fast-forward merge failed: `main` has commits not on `dev`. Re-align by reverting the extra commits or merge `main` into `dev`, then try again.
- Working tree not clean: commit or stash changes before running.
- Protected branch: if `main` blocks direct pushes, use the PR-only flow (`-PrOnly`) and tag after merge.

### Related
- Local pack/push (no tagging): `./scripts/pack-and-push.ps1`
- Publishing docs: `docs/support/nuget-publish.md`
