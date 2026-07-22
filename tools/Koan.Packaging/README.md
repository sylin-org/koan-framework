# Koan.Packaging

Repository-local package inventory and product-surface inspection.

```powershell
dotnet run --project tools/Koan.Packaging -- inventory
dotnet run --project tools/Koan.Packaging -- quality
dotnet run --project tools/Koan.Packaging -- product-surface --check
dotnet run --project tools/Koan.Packaging -- api-baselines
dotnet run --project tools/Koan.Packaging -- admission --id owner:behavior --project tests/Owner.Tests.csproj --filter FullyQualifiedName=Owner.Behavior
dotnet run --project tools/Koan.Packaging -- native-admission --base BASE_SHA --candidate MERGE_SHA
dotnet run --project tools/Koan.Packaging -- terminal-outcomes
```

`inventory` evaluates packable MSBuild projects and requires one local `version.json` per package.
`quality` reports package metadata and documentation posture. `product-surface` compiles the declared
public capability surface and check mode rejects generated drift. `api-baselines` verifies supported
assembly packages against the earliest public 0.20 baseline. `admission` executes one exact ordinary
test selection with a deadline and requires every TRX result to be present and passed.
`native-admission` derives affected claim cells and binds their results (or a machine-derived N/A) to
one clean, exact merge-candidate checkout.
`terminal-outcomes` reconciles ARCH-0120's fixed 55-owner table with active supported product truth
and the bounded removed-owner certificate; `--final` requires every owner to be resolved.

The tool does not calculate a release wave, change Git, pack artifacts, publish packages, or access a
credential. Admission results use an owned system-temporary directory that is always removed. NuGet
publication is the single manual GitHub Actions workflow documented in
[NuGet publishing](../../docs/engineering/nuget-publishing.md).
