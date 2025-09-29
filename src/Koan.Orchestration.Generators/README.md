# Koan.Orchestration.Generators

## Contract
- **Purpose**: Supply Roslyn analyzers and source generators that light up Koan orchestration features with zero-boilerplate diagnostics.
- **Primary inputs**: Projects referencing Koan orchestration assemblies, `KoanAutoRegistrar` implementations, annotated adapters.
- **Outputs**: Generated partial classes, diagnostic warnings for misconfigured modules, and incremental source for registry wiring.
- **Failure modes**: Missing analyzer reference, unsupported language version, or generators running on trimmed assemblies without metadata.
- **Success criteria**: Developers receive actionable diagnostics (e.g., missing adapter capability), generated code compiles cleanly, and analyzers respect incremental build performance.

## Quick start
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Sylin.Koan.Orchestration.Generators" Version="0.6.2" PrivateAssets="all" />
  </ItemGroup>
</Project>
```
```csharp
[KoanModule]
public sealed class MyModuleAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "MyModule";
    public void Initialize(IServiceCollection services) { /* ... */ }
    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env) { }
}
```
- Add the analyzer package as a `PrivateAssets=all` reference to enable diagnostics without distributing it downstream.
- Generators emit hints when `IKoanAutoRegistrar` implementations miss required metadata or capabilities.

## Configuration
- No runtime configuration required; analyzers follow MSBuild conventions.
- Suppress specific diagnostics via `NoWarn` or `[SuppressMessage]` attributes as needed.
- Ensure projects compile with the repository default language version (`latestMajor`).

## Edge cases
- Large solutions: incremental generators minimize overhead, but monitor build times and disable specific analyzers via `.editorconfig` if needed.
- Generated code conflicts: clean intermediate folders if stale generated sources remain after upgrades.
- CI builds: include `-warnaserror` to prevent shipping when orchestration diagnostics detect misconfiguration.
- Analyzer version mismatch: update the package reference to match your Koan release to stay in sync with schema expectations.

## Related packages
- `Koan.Orchestration.Abstractions` – types inspected by the analyzers.
- `Koan.Orchestration.Cli` – benefits from analyzer hints when generating CLI descriptors.
- `Koan.Core` – base constructs validated by diagnostics.

## Reference
- `AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md` – diagnostic catalog and release history.
- `Koan.Orchestration.Generators.csproj` – packaging metadata including analyzer assets.
