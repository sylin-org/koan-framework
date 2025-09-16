# Packaging

This folder contains nuspecs for the meta packages (published under Sylin.Koan.*):

- Koan.nuspec -> Sylin.Koan: meta-package for core + data abstractions + JSON adapter
- Koan.App.nuspec -> Sylin.Koan.App: meta-package for Sylin.Koan + Sylin.Koan.Web

Automated publish

- Stable releases: push a tag vX.Y.Z to trigger `.github/workflows/nuget-release.yml` (publishes to nuget.org).
- Nightly canaries: currently disabled. Enable `.github/workflows/canary-nightly.yml` when needed.

Required secrets

- NUGET_API_KEY: an API key from nuget.org with push scope.

Manual pack (local):

```powershell
# from repo root
# 1) Pack all library projects
dotnet pack . -c Release -o .\artifacts

# 2) Compute version and pack meta-packages with aligned dependency ranges
powershell .\.github\scripts\pack-meta.ps1 -Version (nbgv get-version -v SimpleVersion) -OutDir .\artifacts
```

Push (example):

```powershell
nuget push .\artifacts\Sylin.Koan*.nupkg -Source https://api.nuget.org/v3/index.json -ApiKey $env:NUGET_API_KEY
```

Notes
- Versioning is powered by Nerdbank.GitVersioning (`version.json`). Tags in the form `vX.Y.Z` are treated as public releases.
- Meta-packages are tokenized and packed by CI to align their dependency ranges with the computed release version.
- Dependency range used by meta-packages: [MAJOR.MINOR.0, MAJOR.(MINOR+1).0)
- Consider adding dotnet new templates that reference these meta packages for an even faster start.
