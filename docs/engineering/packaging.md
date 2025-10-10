---
type: ENGINEERING
domain: engineering
title: "NuGet packaging policy"
audience: [developers, maintainers]
status: current
last_updated: 2025-10-09
framework_version: v0.6.3
validation:
	status: drafted
	scope: docs/engineering/packaging.md
---

# NuGet packaging policy

## Contract
- **Scope**: All projects under `src/` that produce NuGet packages or dotnet tools.
- **Inputs**: `version.json`, `apply-version.ps1`, project metadata (`Description`, `PackageTags`), per-package README files.
- **Outputs**: Consistent NuGet metadata, validated packaging artifacts, and CI enforcement via `scripts/validate-packages.ps1`.
- **Failure modes**: Missing descriptions, misaligned tags, absent README files, or manual version drift.
- **Success criteria**: Every packable project ships with aligned metadata, README, and passes the packaging validation script.

## Versioning workflow
- Update `version.json` when preparing a release.
- Run `./apply-version.ps1` (or the `apply-version` CI step) to stamp `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` across projects.
- Do not edit version elements directly inside `.csproj` files; the script ensures consistency.

## Required project metadata
Every packable project **must** provide the following in its primary `<PropertyGroup>`:

- `<Description>` – concise summary (120 characters or less when possible).
- `<PackageTags>` – always include `$(CommonPackageTags)` plus module-specific tags.
- `<GenerateDocumentationFile>true</GenerateDocumentationFile>` for libraries that surface public APIs.
- No custom `<LangVersion>` overrides unless absolutely required; if used, the value must remain `latestMajor` and be documented in the README.

Avoid overriding repository defaults for:

- `<PackageId>` (defaults to `Sylin.$(AssemblyName)`), unless a non-Sylin ID is intentionally required.
- `<Authors>`, `<Company>`, `<PackageLicenseExpression>`, `<RepositoryUrl>`, and symbol packaging options. These are centrally defined in `Directory.Build.props`.

## README expectations
- Each packable project ships with a `README.md` in the project root.
- Document the module contract, configuration, and edge cases following the Koan documentation posture (instruction-first, not a tutorial).
- Include at least one sample leveraging Koan entity static methods or controller patterns.

## Special packaging surfaces
- **Dotnet tools**: Set `<PackAsTool>true</PackAsTool>` and provide `ToolCommandName`. Ensure the README includes install/usage guidance.
- **Analyzers/source generators**: Disable `IncludeBuildOutput`, add explicit analyzer assets under `analyzers/dotnet/cs`, and keep release notes in `AnalyzerReleases.*`.
- **Provider modules**: Describe capabilities, configuration, and environment prerequisites so consumers can wire them quickly.

## Validation script
- Run `scripts/validate-packages.ps1` locally before opening a PR.
- CI pipelines should execute the same script to block regressions.
- The script validates descriptions, tags, language version overrides, and README presence for every packable project.

## Related decisions
- [BUILD-0072](../decisions/BUILD-0072-script-owned-versioning.md) – script-owned versioning policy.

## Next steps
- Add additional lints (e.g., ensuring README contract sections) as packaging matures.
- Integrate validation into `dotnet build` pipeline via a pre-pack step.
