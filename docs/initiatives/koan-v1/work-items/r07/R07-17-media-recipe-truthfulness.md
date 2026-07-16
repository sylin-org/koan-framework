---
type: SPEC
domain: framework
title: "R07-17 - Media Recipe Truthfulness"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.18.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: Media recipe election, startup validation, runtime facts, and unsupported lifecycle promises
---

# R07-17 — Media recipe truthfulness

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
- Depends on: R07-16
- Unlocks: the next evidence-backed capability election
- Owner: Media recipe discovery, startup validation, rendering, and public contract

## Meaningful outcome

Media keeps the behavior applications actually use: an Entity-backed original, named recipes, on-demand
rendering, persisted derivations when the selected source supports them, and inspectable startup decisions.
It no longer advertises upload-time prewarming or a generic scheduled orphan sweep that the default source
never executed safely.

```csharp
public sealed class Photo : MediaEntity<Photo> { }

[MediaRecipe("card", Description = "320px JPEG card")]
public static MediaRecipe Card() => MediaRecipe.New()
    .Resize(width: 320)
    .EncodeAs("jpeg", Quality.Web)
    .Build();

// application composition
services.AddMediaSource<Photo>();
```

`AddKoan()` materializes and validates the recipe catalog during host startup. The same catalog is projected
into runtime facts for startup, operator, and agent inspection.

## Semantic election

- **Not elected:** `entity.Media`, `entities.Media`, query-stream Media, `Prewarm`, or `Derive`.
- **Reason:** no current consumer establishes a distinct pointwise business operation with bounded work and
  fixed-size outcomes. The real in-process consumer needs rendered bytes, while the HTTP consumer needs
  negotiated serving and persistence; pretending these are one verb would hide materially different costs.
- **Future admission rule:** require at least two real consumers of one reusable rendering coordinator before
  projecting it through Entity cardinalities.

## Architecture

- `MediaRecipeRegistry` remains the single recipe decision across attributes, configuration overrides, format
  shortcuts, controller resolution, JSON inspection, and runtime facts.
- `Koan.Media.Core` uses the standard `KoanModule.Start` seam to materialize the registry before startup
  completes. Invalid declarations fail host start; the facts contributor remains correctly fail-soft.
- `MediaCompositionContributor` reports recipe count, producible shortcuts, source, version, fingerprint,
  steps, mutators, and output format posture without creating another catalog.
- `IMediaSource` retains optional derivation read/write because both the controller and the default Entity source
  exercise it. Lifecycle cleanup is deliberately not generalized until source existence can be evaluated safely
  under tenancy and access axes.

## Principal deletion

- `MediaRecipe.Eager`, `MediaRecipeAttribute.Eager`, `MediaRecipeBuilder.WithEager`, and configuration/JSON
  echoes of a prewarm workflow that did not exist;
- `MediaDerivationSweepService`, its options/result, and `IMediaSource.SweepOrphanedDerivationsAsync`, whose
  default was a no-op and whose context-free source probes are unsafe for access-scoped applications;
- unused `RoutePrefix` and immutable-cache options for unimplemented route shapes; and
- an unused `Koan.Cache` dependency in `Koan.Media.Web`.

No compatibility aliases are retained. These are pre-1.0 corrections from declared fiction to supported code.

## Current lifecycle boundary

- The default `MediaEntitySource<TEntity>` stores rendered derivatives as framework-owned
  `MediaDerivation` records after gating the source Entity through active data axes.
- Source deletion makes an orphan unreachable but does not reclaim its storage automatically.
- Applications that own source deletion must perform targeted cleanup until Media owns one trustworthy,
  context-aware lifecycle chokepoint. The maintained sample demonstrates that explicit boundary.
- A full split between source resolution and derivative storage is deferred until the reusable rendering
  coordinator gives the split a real second consumer.

## Delight contract

- Developers declare one Entity, recipes, and one source registration; they do not write a rendering controller.
- Coding agents can inspect the exact materialized recipes and bounds instead of inferring capability from stale
  flags or comments.
- Operators see recipe source, version, fingerprint, mutators, and output posture at startup; invalid recipes stop
  the host instead of failing on first traffic.
- Unsupported prewarming and cleanup are stated directly, avoiding false confidence in production lifecycle
  behavior.

## Acceptance

- invalid recipes fail a real host start;
- valid recipes appear in the shared runtime fact envelope;
- current Core and Web Media suites pass after deleting inert surfaces and their self-fulfilling tests;
- the maintained Media sample builds without prewarm declarations;
- public reference and package companions describe only current types, routes, options, and limits;
- touched packages build strictly and pack with truthful companions and independent versions; and
- focused docs, examples, diff, stale-surface, and privacy gates pass without release certification.

## Evidence

- Media Core passes 562/562 after deleting five self-fulfilling generic-sweep tests.
- The real hosted Media Web suite passes 4/4: access-gated source reads, persisted derivative round-trip,
  attribute plus configuration recipe facts, and invalid configuration failing host startup.
- `Koan.Media.Abstractions`, `Koan.Media.Core`, and `Koan.Media.Web` build warning-as-error with zero
  warnings/errors. The maintained photo sample builds with zero errors and seven unrelated historical warnings.
- All three packages pack at 0.18.0 with DLL, XML documentation, package README, symbols, and bounded Koan
  dependency ranges. Inspection confirms Web no longer carries the stale Cache dependency after a fresh restore.
- Package inventory remains 112 independently versioned owners.
- Docs lint reports 0 errors / 1581 historical or version-front-matter warnings; skills lint passes 20/20;
  the changed marked Media example compiles 1/1. Diff, stale-surface, and privacy checks pass.
- No full-solution or public-release certification suite ran.

## Acceptance result

- Outcome: PASS
- Date: 2026-07-16
- Follow-up: audit the R07 parent exit criteria before admitting another capability slice.
- Reviewer: Codex implementation under maintainer standing approval.
