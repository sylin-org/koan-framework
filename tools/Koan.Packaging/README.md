# Koan.Packaging

Repository-local package inventory and product-surface inspection.

```powershell
dotnet run --project tools/Koan.Packaging -- inventory
dotnet run --project tools/Koan.Packaging -- quality
dotnet run --project tools/Koan.Packaging -- product-surface --check
```

`inventory` evaluates packable MSBuild projects and requires each one to inherit the root `version.json`.
`quality` reports package metadata and documentation posture. `product-surface` compiles the declared
public capability surface; check mode rejects drift in the checked-in Markdown projection. JSON is
generated on demand for release automation instead of checked in twice. Its package list is the
complete release inventory. Claims document evidence and maturity; they do not select which packages
ship. Supported claims must include the complete public package dependency closure.

The tool does not change Git, pack artifacts, publish packages, query NuGet, or access a credential.
NuGet publication is the single GitHub Actions workflow documented in
[NuGet publishing](../../docs/engineering/nuget-publishing.md).
