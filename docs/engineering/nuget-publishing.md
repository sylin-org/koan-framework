---
type: ENGINEERING
domain: framework
title: "Release playbook"
audience: [maintainers, release-engineers, ai-agents]
status: current
last_updated: 2026-08-17
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-22
  status: reviewed
  scope: front matter normalized against the docs lint; prose not re-verified
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

Freely, for now. **Koan 1.x is the stabilization line and the framework is not announced**, so the published
1.0.0 packages have no consumers and a removed public member costs nothing. Delete what is not needed; keep the
surface lean.

Baseline validation is therefore **off** — `KoanHasPublishedBaseline` in `Directory.Build.targets`. It had been
comparing every assembly against 1.0.0 and reporting 101 differences, all of them deliberate design from one
stabilization cycle. Recording those as suppressions would have been bookkeeping for an audience of zero.

Turn it back on at announcement, with `KoanTrainBaselineVersion` set to whatever is published then. From that
point a removed public member is a real cost, and the suppression flow is the way to record a deliberate one:

```powershell
dotnet pack Koan.sln -c Debug -p:ApiCompatGenerateSuppressionFile=true
```

Two things to know before relying on it. It runs at **pack** and never at build, so a removed member leaves a
green solution and an unshippable package unless CI packs deliberately. And a type that merely moves assemblies
needs no entry at all — `[assembly: TypeForwardedTo(...)]` keeps the name resolving.

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

## Code signing policy

Primary packages are prepared for publisher signing through SignPath Foundation after packing and
before the package-only consumer proof. The signed package set must match the release plan exactly;
the publication job receives only that certified feed. Symbol packages keep their existing path.

Signing remains transitional while the open-source subscription is under review. With repository
variable `SIGNPATH_ENABLED` unset or false, publication continues with unsigned publisher packages.
After it is set to true, missing SignPath configuration or an incomplete, invalid, or rejected
signed package set stops the release. Every package release note links the policy; the SignPath
attribution is added only when signing is enabled.

The repository requires these values when signing is enabled:

- secret `SIGNPATH_API_TOKEN`;
- variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`,
  `SIGNPATH_SIGNING_POLICY_SLUG`, and `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`;
- the artifact configuration in
  [`signpath-artifact-configuration.xml`](signpath-artifact-configuration.xml), copied into the
  corresponding SignPath project.

See the public [Code signing policy](../../CODE_SIGNING_POLICY.md) for roles, scope, privacy,
removal, and verification.
