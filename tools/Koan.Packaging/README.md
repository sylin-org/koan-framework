# Koan.Packaging

Repository-local package inventory and product-surface inspection.

```powershell
dotnet run --project tools/Koan.Packaging -- inventory
dotnet run --project tools/Koan.Packaging -- quality
dotnet run --project tools/Koan.Packaging -- product-surface
```

`inventory` evaluates packable MSBuild projects and requires one local `version.json` per package.
`quality` reports package metadata and documentation posture. `product-surface` compiles the declared
public capability surface.

The tool does not calculate a release wave, change Git, pack artifacts, publish packages, or access a
credential. NuGet publication is the single manual GitHub Actions workflow documented in
[NuGet publishing](../../docs/engineering/nuget-publishing.md).
