---
id: BUILD-0072
status: accepted
date: 2025-09-29
related:
  - tools/versioning-script
---

## Contract
- **Decision scope**: Koan repository version stamping for NuGet packages and assemblies.
- **Inputs**: `version.json`, release versioning script (`scripts/apply-version.ps1`), MSBuild project files.
- **Outputs**: Version metadata applied to project files prior to packaging; consistent NuGet versions.
- **Error modes**: Stale manual `<Version>` entries, mismatched assembly/file versions, duplicate version sources.
- **Success criteria**: Single source of truth, automated during release, no redundant tooling.

## Context
Koan releases rely on a repository script that reads the root `version.json` file, updates project files, and prepares artifacts. Nerdbank.GitVersioning was introduced earlier but is not invoked by the release process, creating duplicate sources of truth and manual cleanup work. Contributors have requested that the script remain authoritative.

## Decision
1. Retain `version.json` as the canonical version manifest for Koan.
2. Remove the Nerdbank.GitVersioning tooling from the build.
3. Eliminate manual `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` entries from packable projects; the versioning script will apply the correct numbers during release preparation.
4. Integrate the versioning script into CI to ensure artifacts cannot be produced with stale metadata.

## Consequences
- Releases update a single file (`version.json`) and execute the script, minimizing human error.
- Build tooling is simplified by removing Nerdbank.GitVersioning dependencies.
- Pull requests that change version metadata without the script will fail validation once the CI guard is in place.
- Future migrations to a different versioning strategy run through the script rather than MSBuild package swaps.

## Follow-up
- Update CI to run the versioning script before `dotnet pack`.
- Add packaging validation to ensure projects do not reintroduce manual version nodes.
- Communicate this policy in contributor documentation.
