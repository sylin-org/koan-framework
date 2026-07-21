---
type: DEV
domain: framework
title: "Package versioning"
audience: [maintainers, release-engineers]
status: current
last_updated: 2026-07-20
framework_version: v0.20.0
---

# Package versioning

Each packable project owns its version independently through a project-local `version.json` and
Nerdbank.GitVersioning (NBGV).

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/Nerdbank.GitVersioning/main/src/NerdBank.GitVersioning/version.schema.json",
  "version": "0.20",
  "versionHeightOffset": -1,
  "pathFilters": ["."]
}
```

- `version` is deliberate major/minor compatibility intent.
- Git height supplies the patch.
- `pathFilters` identifies source that changes the package's bits. Include a sibling path directly
  when the package embeds output built from that sibling.
- `PublicRelease=true` produces the stable public identity without a local commit suffix.

Preview a package before release:

```powershell
dotnet nbgv get-version -p src/Koan.Core --public-release=true
```

For an ordinary patch, change the package-owned source and leave `version.json` alone. For a pre-1.0
breaking compatibility change, advance the minor; after 1.0, advance the major. Do not set MSBuild
`Version`, hand-edit patches, or use tags to influence package identity.

Every packable project must have its own `version.json`. Repository inventory fails correctively when
one is missing or malformed:

```powershell
dotnet run --project tools/Koan.Packaging -- inventory
```

See [NuGet publishing](nuget-publishing.md) for the explicit release action.
