# Package versioning

Koan packages version independently with Nerdbank.GitVersioning (NBGV). A package's `version.json`
contains deliberate major/minor intent; Git history supplies the patch. Advancing `dev` is the release
event, and the release compiler compares the public NBGV version at the event's two commits.

> Governing decisions: [ARCH-0085](../decisions/ARCH-0085-versioning-compatibility-and-automation.md)
> and [ARCH-0110](../decisions/ARCH-0110-dev-release-compiler.md).

## Mental model

```text
package-local version.json + commits matching pathFilters
                         ↓
               deterministic NBGV version
                         ↓
before version differs from after version? ── yes ──> release that package identity
                         │
                         no
                         ↓
                 leave it untouched
```

Every packable project must have a `version.json` in its own directory. There is no kernel lockstep,
root stamping pass, or package checklist. A typical file is:

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/Nerdbank.GitVersioning/main/src/NerdBank.GitVersioning/version.schema.json",
  "version": "0.17",
  "versionHeightOffset": -1,
  "pathFilters": ["."]
}
```

- `version` is the semantic major/minor floor. Change it only when you mean to change the
  compatibility tier.
- patch is the matching Git height and is never typed by hand;
- `pathFilters` defines the package-affecting history. Most leaf packages own their directory;
  bundles additionally include the paths that define their composition;
- `versionHeightOffset` establishes a baseline. `-1` makes a newly introduced ownership commit
  `.0`. Some migrated, already-existing packages deliberately use `0` so the first dedicated
  ownership commit cannot collide with an already published `.0`; preserve those baselines.

The workflow passes `PublicRelease=true`, so a `dev` event produces a stable package identity. Local
non-release builds may carry NBGV's commit suffix.

## Common tasks

### Preview one package

```powershell
dotnet nbgv get-version -p src/Koan.Core --public-release=true
```

### Preview the exact event

```powershell
dotnet run --project tools/Koan.Packaging -- plan `
  --before HEAD~1 --after HEAD --offline `
  --output artifacts/release/release-set.json
```

The offline form shows only identities changed by Git. The protected workflow uses the online form,
which also includes a current identity missing from nuget.org so interrupted or historical gaps heal
without an operator-maintained list.

### Make a normal patch release

Change business/framework code in the package's owned path and push or merge it to `dev`. Do not edit
the version file for an ordinary patch.

### Change the compatibility tier

Edit that package's `version.json` `version` field as part of the breaking/feature change. Before 1.0,
the minor is the breaking tier; at 1.0 and later, the major is. Internal dependencies pack as bounded
ranges (`[0.17.x,0.18.0)` before 1.0), so incompatible combinations fail during restore.

ARCH-0085's reverse-dependent republish rule for a breaking tier change remains explicit follow-up
work. Do not describe that closure as automated until its version-graph gate exists.

### Add a package

Create its project-local `version.json`, README, metadata, and ProjectReferences, then run:

```powershell
dotnet run --project tools/Koan.Packaging -- inventory
```

Inventory fails when a packable project lacks a local version owner or two projects claim one package
ID.

## Failure → recovery

- **Unexpected prerelease suffix** — preview with `--public-release=true`; never compensate with a
  `<Version>` property.
- **Unexpected patch** — inspect commits matching `pathFilters` and the package's intentional
  baseline. Patch height counts Git history, including merge commits.
- **Package absent from a release plan** — confirm its evaluated `IsPackable`, local `version.json`,
  and whether the two endpoint versions actually differ.
- **Current version is missing publicly** — use an online plan; reconciliation includes it even if
  the event itself did not change its version.
- **Duplicate identity** — do not force-push or overwrite. Package identities are immutable; advance
  the package through Git.

## Anti-patterns

- Do not set `<Version>`, `<PackageVersion>`, `<AssemblyVersion>`, or `<FileVersion>` per project.
- Do not hand-edit a patch number or run `apply-version`.
- Do not delete a package-local `version.json`; that destroys independent ownership.
- Do not copy one version into a bundle's dependency ranges.
- Do not tag to influence NBGV or trigger publication; release tags are post-publication evidence.

See [packaging.md](packaging.md) for the package contract and
[nuget-publishing.md](nuget-publishing.md) for the automated release path.
