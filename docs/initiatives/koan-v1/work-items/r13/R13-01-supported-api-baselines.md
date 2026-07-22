---
type: SPEC
domain: framework
title: "R13-01 - Protect Supported 0.20 APIs"
audience: [maintainers, package-authors, ai-agents]
status: current
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: passed
  scope: 35 exact public API baselines, second-patch guard, SDK pack validation, and content-only exclusion
---

# R13-01 — Protect supported 0.20 APIs

- Status: `passed`
- Depends on: accepted R13 bootstrap plan
- Unlocks: R13-02 PR product-surface drift enforcement
- Owner: project-local standard SDK package validation plus a bounded guard in the existing packaging tool/main publisher

## Meaningful outcome

A supported assembly package can publish its first deliberate 0.20 artifact, but every later 0.20
patch is validated against that immutable first artifact. Existing supported packages receive their
actual first public 0.20 version without reopening capability evidence. Content-only packages keep
their existing artifact/dependency-shape and isolated-consumer protections.

## Entry gate

**Application intent:** A package consumer upgrades within 0.20 without a silently removed or changed
public assembly contract.

**Public expression:** Package authors continue editing the owning csproj and `version.json`; after the
first 0.20 artifact is public, the csproj records standard `PackageValidationBaselineVersion`. No
application C# expression or new Koan API is introduced. The existing `main` publisher permits a
missing baseline only when NuGet has no stable 0.20 artifact for that newly admitted package.

**Guarantee/correction:** Every already-published supported assembly-bearing owner has its earliest
stable `0.20.patch` baseline and SDK package validation runs on pack. A newly admitted owner may publish
its first 0.20 artifact without a nonexistent baseline; once any public 0.20 exists, the publisher
rejects every later candidate until that exact earliest version is configured. A genuine API break
advances the compatibility tier; missing/invalid/mismatched baseline configuration names the package
and owning project. Content-only owners are excluded from API comparison and remain covered by package
shape and consumer evidence.

**Complete intent surface:** 38 supported package owners from compiled claims; 35 evaluated owners with
`IncludeBuildOutput=true`; three content-only owners; project-local csproj/version files; NBGV public
package version; `Directory.Build.targets`; `RepositoryInspector`; `PackageProject`; NuGet's read-only
flat-container index; the existing `main` publisher; and focused policy, pack, and package-consumer tests.

**Public concepts:** Only the SDK's `PackageValidationBaselineVersion` and
`EnablePackageValidation`; both directly express the compatibility guarantee.

**Coalescence:** Keep baseline identity in the owning project and enable the platform validator once
from `Directory.Build.targets`, after each project-local declaration. Extend evaluated package facts only enough for a focused validator in the
existing packaging tool to join compiled supported claims with public baseline availability. Invoke it
from the one accepted publisher before pack. Do not make product truth depend on NuGet or create an API
manifest, suppression ledger, or central package allowlist. Disposition: keep/reuse standard MSBuild;
add the smallest publication guard at its existing chokepoint.

**Ergonomics:** One recognizable MSBuild property per assembly package, no application ceremony, and
an error that names the exact owner and correction. The three content packages do not carry a
misleading API property.

## Exact placement

| Change | Location | Reason |
|---|---|---|
| conditional SDK validation policy | `Directory.Build.targets` | one standard build-policy owner evaluated after every project-local baseline declaration |
| immutable first-0.20 version | each of the 35 supported assembly-owner csproj files | project-local compatibility ownership required by ARCH-0120 |
| evaluated baseline/activation facts | `tools/Koan.Packaging/Models/PackageProject.cs` and `Services/RepositoryInspector.cs` | reuse the existing evaluated-project truth boundary |
| first-publication/second-patch guard | focused service and command under `tools/Koan.Packaging` | join compiled supported owners to immutable public availability without changing product truth |
| pre-pack invocation | `.github/workflows/release-on-main.yml` | the only accepted publication chokepoint already depends on NuGet availability |
| stable messages/format | `tools/Koan.Packaging/Infrastructure/PackagingConstants.cs` | no new scattered literals |
| focused mutation/evaluation proof | `tests/Koan.Packaging.Tests` | closest accepted compiler, evaluated-MSBuild, and policy patterns |

## Immutable baseline evidence

Public NuGet's read-only flat-container index reports exactly one stable 0.20 artifact for each of the
35 assembly packages as of 2026-07-21. The baseline versions range from 0.20.1 through 0.20.8; the
implementation records the exact package-specific first value, never a guessed common patch.

## Focused verification

- focused baseline policy, product-surface, and version-intent tests using a fake public index;
- evaluated real inventory: 38 supported, 35 assembly-bearing, three content-only;
- focused `PublicRelease=true` packs that prove platform baseline comparison is active and content-only
  packs remain valid;
- isolated package-consumer/shape tests already owned by the package-first claim where affected;
- `git diff --check` and changed-document validation.

## Stop conditions

- Stop if SDK validation cannot consume the immutable public artifacts honestly or public availability cannot be checked fail-closed.
- Stop rather than suppressing a genuine API break into 0.20.
- Stop if enforcement needs a central version/package list or freezes a below-0.20 owner early.

## Implementation and evidence

- Repository evaluation confirms 38 supported owners: 35 with assembly output and three content-only
  bundles/templates. Only the 35 assembly owners now declare project-local SDK baselines.
- Public NuGet verification resolved each configured value to that package's exact earliest stable
  0.20 artifact: all 35 configured, zero first-publication pending, and three content-only.
- `Directory.Build.targets` activates standard `EnablePackageValidation` after each project-local
  declaration is evaluated. `RepositoryInspector` carries the evaluated baseline/activation facts.
- `Koan.Packaging api-baselines` compiles the supported product surface, checks public availability
  fail-closed, rejects a missing baseline after the first public 0.20 artifact, and rejects any later
  patch used as the compatibility floor. The existing `main` publisher runs this guard before pack.
- A focused real `Sylin.Koan.Core` public-release pack restored baseline `0.20.4`, produced the SDK
  ApiCompat semaphore, and created the candidate package successfully. The content-only `Sylin.Koan`
  bundle packed successfully without a baseline.

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-21; intentionally uncommitted local R13 slice
- Application intent and complete public expression: package consumers retain the admitted 0.20 API;
  package authors use standard project-local baseline metadata after first publication
- Guarantee / correction: every later 0.20 pack validates against the earliest immutable artifact;
  a second public patch without that baseline fails before pack
- Coalescence disposition: standard SDK validation plus one bounded guard in the existing packaging
  tool/main publisher; no API manifest, allowlist, or release coordinator
- Ergonomics proof: one standard property per assembly owner; no application-facing concepts; errors
  name package, project, public artifact, and correction
- Evidence: `api-baselines` reports `35/35 configured`, `0 first-publication pending`, `3 content-only`;
  generated product JSON/Markdown remain byte-identical
- Tests / validation: baseline policy 7/7; focused packaging 35/35; warning-clean Release tool build;
  Core SDK-validation pack; content-only bundle pack; `git diff --check`
- Unsupported scenarios: first 0.20 publication intentionally has no nonexistent baseline; content-only
  packages use artifact/dependency-shape and isolated-consumer checks
- Follow-up work: R13-02 adds the real product compile/drift check and earlier baseline feedback to the
  `main` PR gate
- Reviewer: pending maintainer review
