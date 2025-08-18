# Packaging

This folder contains nuspecs for the meta packages (published under Sylin.Sora.*):

- Sora.nuspec -> Sylin.Sora: meta-package for core + data abstractions + JSON adapter
- Sora.App.nuspec -> Sylin.Sora.App: meta-package for Sylin.Sora + Sylin.Sora.Web

Automated publish

- Stable releases: push a tag vX.Y.Z to trigger `.github/workflows/nuget-release.yml` (publishes to nuget.org).
- Nightly canaries: `.github/workflows/canary-nightly.yml` publishes changed projects with a `ci.<run>` suffix to GitHub Packages.

Required secrets

- NUGET_API_KEY: an API key from nuget.org with push scope.

Manual pack (local):

```powershell
# from repo root
nuget pack .\packaging\Sora.nuspec -OutputDirectory .\artifacts
nuget pack .\packaging\Sora.App.nuspec -OutputDirectory .\artifacts
```

Push (example):

```powershell
nuget push .\artifacts\Sylin.Sora*.nupkg -Source https://api.nuget.org/v3/index.json -ApiKey $env:NUGET_API_KEY
```

Notes
- Versioning is powered by Nerdbank.GitVersioning (`version.json`). Tags in the form `vX.Y.Z` are treated as public releases.
- Meta-packages are tokenized and packed by CI to align their dependency ranges with the computed release version.
- Consider adding dotnet new templates that reference these meta packages for an even faster start.
