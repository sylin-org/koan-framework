# Koan.Packaging

Repository-local package inventory and product-surface inspection.

```powershell
dotnet run --project tools/Koan.Packaging -- inventory
dotnet run --project tools/Koan.Packaging -- quality
dotnet run --project tools/Koan.Packaging -- product-surface --check
dotnet run --project tools/Koan.Packaging -- api-baselines
```

`inventory` evaluates packable MSBuild projects and requires one local `version.json` per package.
`quality` reports package metadata and documentation posture. `product-surface` compiles the declared
public capability surface and check mode rejects generated drift. `api-baselines` verifies supported
assembly packages against the earliest public 0.20 baseline. Product claims retain product judgment
and point to family-owned evidence; ordinary test projects and direct provider workflows execute that
evidence without a second admission vocabulary.

The tool does not calculate a release wave, change Git, pack artifacts, publish packages, or access a
credential. NuGet publication is the single manual GitHub Actions workflow documented in
[NuGet publishing](../../docs/engineering/nuget-publishing.md).
