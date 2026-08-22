---
type: DEV
domain: framework
title: "Release playbook"
audience: [maintainers, release-engineers, ai-agents]
status: current
last_updated: 2026-08-17
framework_version: v1.0.0
---

# Release playbook

`main` is the published state. Fast-forward `main` from `dev` and Koan publishes whatever changed
since the last release — nothing more, nothing less.

## The one rule

**You never set a version.** Each packable project owns a `version.json` and its patch comes from the
commits that touched it. A release is a set difference: every project's computed version, minus the
versions nuget.org already has. Change code, merge to `dev`, fast-forward `main`.

A merge that changed no packable source publishes nothing and is a green no-op.

## Prerequisites

- Repository secret `NUGET_API_KEY` holds the nuget.org push key. Only the final publish job receives it.
- `main` and `dev` carry the "Koan release trust boundary" ruleset: no deletion, no force-push,
  linear history required.
- Work reaches `main` only by fast-forward from `dev`.

## Release

```powershell
git checkout main
git merge --ff-only dev
git push origin main
```

Then watch the **Release** workflow. Never publish from a workstation.

If `--ff-only` refuses, `main` and `dev` have diverged — see [Recovery](#recovery).

## Know what will ship, before you push

The plan is reproducible locally and touches nothing:

```powershell
dotnet run --project tools/Koan.Packaging -- inventory --output artifacts/release/inventory.json
./scripts/plan-release.ps1 -InventoryPath artifacts/release/inventory.json -OutputPath artifacts/release/release-plan.json
```

It prints one line per package and a summary:

```
PLAN|Sylin.Koan.Observability|1.0.1|new
RELEASE-PLAN|train=1.0|inventory=94|publish=1
```

`publish=0` means nothing changed and the release will be a no-op.

## What the workflow does

| Step | Script | Proves | Marker |
|---|---|---|---|
| Guard | — | the pushed commit is reachable from `origin/dev` | step fails otherwise |
| Plan | `plan-release.ps1` | each version, and which are absent from nuget.org | `RELEASE-PLAN\|train=…\|publish=N` |
| Pack | `pack-release.ps1` | only the changed set builds and packs | `PACK\|PACKED\|N` |
| Prove | `verify-release.ps1` | a package-only app restores from the feed **plus** nuget.org, builds, and runs | `VERIFY\|OK\|resolved=…\|staged=…` |
| Publish | `publish-release.ps1` | the planned packages reach nuget.org, dependencies first | `PUBLISH\|DONE\|N` |

The publish job runs separately so the credential never exists in the environment that built the
packages. **Nothing is published unless every earlier step passed.**

An unchanged package is never rebuilt. The build stamps the current commit into the assembly, so
rebuilding unchanged sources at a later commit would produce different bytes under an already
published version — see [ARCH-0125](../decisions/ARCH-0125-per-project-package-versions.md).

## Adding a new package

A new packable project needs its own `version.json` before it can join the inventory. Copy one from a
sibling and set `versionHeightOffset` to `0` so it starts at the train's `.0`. The packaging tool
rejects a packable project that inherits an ancestor `version.json`, so this cannot be missed
silently.

## Breaking something inside 1.x

**Koan 1.x is the stabilization line.** A public surface may still be removed or reshaped inside it, and the
rule is that the break is *recorded* rather than prevented.

Package validation compares every packable assembly against its published baseline —
`KoanTrainBaselineVersion`, currently `1.0.0` — and fails on `CP0001` (a type is gone) or `CP0002` (a member
is gone). **It runs at pack time only.** `dotnet build` cannot see it, so removing a public member leaves the
solution green and the package unshippable, and nothing says so until a release is attempted. Before deleting
any public surface, pack its project:

```powershell
dotnet pack src/<Project>/<Project>.csproj -c Debug
```

When the break is intended, record it:

```powershell
dotnet pack Koan.sln -c Debug -p:ApiCompatGenerateSuppressionFile=true
```

That writes a `CompatibilitySuppressions.xml` next to each affected project. Commit it with the change that
caused it, never separately — a suppression whose cause is a different commit is a break nobody can explain
later.

A suppression is a record of debt, not permission to take more. Two things follow. A removal that is merely
tidying is rarely worth spending compatibility on, because the entry outlives the tidying. And when the line
moves, these files are **deleted** rather than carried forward: the new baseline already contains the new
shape.

One case needs no suppression at all. A type that moves between assemblies keeps its name with
`[assembly: TypeForwardedTo(...)]`, so nothing compiled against the old package stops resolving — see
`src/Koan.Data.Relational/AssemblyInfo.cs`. Local validation cannot always confirm it, because ApiCompat
follows a forward only when it can resolve the target assembly and a sibling prerelease is on no feed; the
forwarder is still the correct thing to ship.

## Moving the compatibility line

This is the **only** time a human edits a version. Moving `1.0` to `1.1` (additive line) or `2.0`
(breaking line) means changing the `version` field in the root `version.json` **and in every
project's `version.json`**, and resetting each `versionHeightOffset` to `0` — changing `version`
restarts NBGV's height lineage, so the old offset would no longer apply.

Do not trust the arithmetic. Verify before pushing:

```powershell
dotnet nbgv get-version -p src/Koan.Core --public-release=true
dotnet nbgv get-version -p src/Koan.AI  --public-release=true
```

Every project must report the new `X.Y.0`. Then advance `KoanTrainBaselineVersion` in
`Directory.Build.targets` to the last fully published version before preparing the new train.

## Recovery

| Failure | Meaning | Do this |
|---|---|---|
| `--ff-only` refuses, or the guard step fails | `main` is not reachable from `dev` — someone merged instead of fast-forwarding, or committed on `main` | Reset `main` to `dev`. Archive first: `git tag archive/main-<date> origin/main && git push origin --tags` |
| Plan fails | a project's version is off-train, or NBGV cannot resolve it | Fix on `dev`; check the project's `version.json` |
| Pack fails | ordinary build failure in a changed project | Fix on `dev` and merge again |
| Prove fails | the real signal: a package does not resolve, or the app does not run | Fix on `dev`. Common cause: a dependency range that excludes an already published package |
| Publish fails | usually transient registry or credential trouble | Rerun **only** the publish job. `--skip-duplicate` makes whatever already landed a no-op |

Failures before the publish step cost nothing — no package has been pushed. A partially completed
publish is safe to rerun: no version is ever republished.

If a released package is wrong, **fix forward**. Never delete, re-push, or reuse a published version.

## Never

- Never hand-edit a package version, or set `<Version>` in a project file.
- Never commit directly on `main`. It only ever fast-forwards from `dev`.
- Never force-push `main` or `dev`; the ruleset blocks it, and it would break version height.
- Never publish from a workstation.
- Never move or reuse a published version.

## Confirm a release landed

```powershell
curl -sI https://api.nuget.org/v3-flatcontainer/sylin.koan.observability/1.0.1/sylin.koan.observability.1.0.1.nupkg
```

`HTTP 200` means the version **exists**. Expect roughly five minutes between a successful push and
that probe turning green while nuget.org validates the package; a `404` in that window is normal.

Existence and installability propagate separately, and this trips people up:

| Probe | Answers | Lags |
|---|---|---|
| the package URL above | does this version exist? | ~5 min after push |
| `dotnet add package` / `dotnet new install` | can a consumer resolve it? | longer — needs the search and registration indexes |

So immediately after a release, `dotnet new install <package>` can still hand you the previous
version while the direct probe says 200. That is propagation, not a failed publish. Wait, or ask for
the exact version with `::<version>`.

`plan-release.ps1` deliberately uses the existence probe. It answers "has this version been
published?", which is what prevents republishing; resolving through the search index would risk
pushing over a version that exists but is not yet indexed.

See [Package versioning](versioning.md), [Packaging](packaging.md), and
[ARCH-0125](../decisions/ARCH-0125-per-project-package-versions.md).
