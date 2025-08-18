## NuGet publishing

This repo publishes packages in two ways:

1) Stable releases to nuget.org
- Trigger: push a Git tag matching vX.Y.Z.
- Workflow: `.github/workflows/nuget-release.yml` computes the NuGet version via Nerdbank.GitVersioning (version.json), packs all libraries and the meta packages, and pushes to nuget.org (duplicates skipped). Symbols (.snupkg) are pushed too.
- Secret required: `NUGET_API_KEY` (org or repo).

2) Nightly canaries to GitHub Packages
- Trigger: nightly schedule or manual run.
- Workflow: `.github/workflows/canary-nightly.yml` finds changed projects on `dev`, packs with a `ci.<run>` suffix, and publishes to `https://nuget.pkg.github.com/<owner>/index.json`.

### Local pack/push helper

Use `scripts/pack-and-push.ps1` to pack everything locally and optionally push.

Examples (PowerShell):

```powershell
# Pack only to ./artifacts with a computed prerelease version
pwsh -File ./scripts/pack-and-push.ps1

# Pack and push to nuget.org (requires $env:NUGET_API_KEY)
pwsh -File ./scripts/pack-and-push.ps1 -Push

# Pack with an explicit version and push to a custom source
pwsh -File ./scripts/pack-and-push.ps1 -Version 0.2.0 -Push -Source 'https://api.nuget.org/v3/index.json'
```

Notes
- Package IDs default to `Sylin.<AssemblyName>` for packable projects; override per project via `<PackageId>`.
- Meta packages (`packaging/Sora.nuspec`, `packaging/Sora.App.nuspec`) are tokenized and CI sets their version and dependency ranges.
- Use tags `vX.Y.Z` for public releases; NB.GV reads from `version.json` and the tag to derive `NuGetPackageVersion`.
