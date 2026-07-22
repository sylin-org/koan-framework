---
type: SPEC
domain: framework
title: "R13-16 - Promote local storage and media"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  status: pending
  scope: Storage runtime, Local provider, Media runtime/Web, packages, consumer, product, and API evidence
---

# R13-16 — Promote local storage and media

## Architecture checkpoint

**Task:** Promote Koan's Storage contracts/runtime, Local provider, and Media contracts/runtime/Web
as one small supported 0.20 journey. S3 and Data Backup are explicitly shelved and do not participate
in this promotion decision.

**Application intent:** An application stores Entity-owned bytes on its local filesystem and renders
named media recipes from storage-backed originals without constructing providers, repositories,
pipelines, or controllers.

**Public expression:**

```powershell
dotnet add package Sylin.Koan.Storage.Connector.Local
dotnet add package Sylin.Koan.Media.Web
```

```csharp
builder.Services.AddKoan().AsWebApi();

[StorageBinding("main")]
public sealed class Document : StorageEntity<Document>;

public sealed class Photo : MediaEntity<Photo>;

public static class PhotoRecipes
{
    [MediaRecipe("card")]
    public static MediaRecipe Card() => MediaRecipe.New()
        .Resize(width: 320)
        .EncodeAs("jpeg", Quality.Web)
        .Build();
}
```

**Guarantee/correction:** Storage compiles exact profile/provider routes once, applies segmentation at
the service chokepoint, and fails correctively for absent providers, invalid profiles, ambiguous
defaults, or unavailable required placement. Local supplies safe filesystem operations. Media
validates recipes at startup and applies bounded controller-owned HTTP rendering with access-gated
Entity source resolution. Invalid recipes and ambiguous media sources fail visibly.

**Complete intent surface:** Install the Local and Media Web packages; call `AddKoan().AsWebApi()`;
declare `StorageEntity<T>`/`MediaEntity<T>` plus optional bindings or recipes; configure the Local
profile root; and select a media source only when several candidates exist. No manual provider,
pipeline, controller, health, or route registration is required.

**Public concepts:** `StorageEntity<T>` maps bytes to Entity-owned business objects;
`[StorageBinding]`, profiles, provider pins, and `StorageMode` express placement; `IStorageService` is
the multi-model infrastructure escape hatch; `MediaEntity<T>`, `[MediaRecipe]`, recipe values, and
pipeline terminals express media intent; `AddMediaSource<T>` resolves only a real source ambiguity.

**Docs/code read:** The governing engineering and architecture principles, Storage and Media
references, `StorageService`, `LocalStorageProvider`, `MediaPipeline`, and `MediaController` were read.
Their existing boundaries remain: Storage owns route meaning, Local owns filesystem IO, Media Core
owns execution, and Media Web owns HTTP projection.

**Reusing:** Existing Storage contracts/runtime/routing, Local provider, typed options and constants,
Media recipes/pipeline/controller, focused Storage/Local/Media suites, package compiler, API guard,
lean PR gate, and main publisher.

**Creating new:** Product claims for Storage+Local and the already-existing Media family, plus this
bounded evidence card. No production runtime type, workflow, certification system, provider harness,
or admission layer is added.

**Coalescence:** The six package owners represent two meaningful capabilities, not six package scores:
Storage+Local is the local persistence foundation; Media Abstractions/Core/Web is the transformation
and serving extension. Existing owners and application grammar are retained.

**Constraints satisfied:** Entity-first storage/media surfaces; controller-owned HTTP; existing typed
options/constants; no placeholder APIs; focused family evidence instead of whole-framework
certification.

## Shelved capabilities

- `Sylin.Koan.Storage.Connector.S3` remains at its current version intent and is not a supported 0.20
  claim in this slice. Real MinIO exploration exposed two useful follow-ups: normalize documented
  HTTP(S) endpoints for the MinIO client and make range behavior portable across compatible services.
  The exploratory production edits and new container suite were removed from this branch.
- `Sylin.Koan.Data.Backup` remains at its current version intent and is not a supported 0.20 claim in
  this slice. Its existing focused evidence is retained, but no publication claim is inferred from it.
- Either capability may return as its own value-led slice; neither blocks Local Storage or Media.

## Evidence boundary

1. Use the already-green Storage Core 20/20, Local 31/31, Media Core 562/562, and Media Web 8/8 owners.
2. Pack the six owners with `PublicRelease=true`; inspect Koan dependency bands and Media native assets.
3. Restore/build/run one clean package-only consumer through `AddKoan()`, Local storage, Media pipeline,
   and Media Web activation.
4. Compile product truth, run API posture and the lean no-tests coherence gate, publish through `main`,
   then rerun the consumer from NuGet.org-only packages.
5. Do not run S3, Backup, unrelated providers, framework-wide certification, or another admission layer.

## Current evidence

- focused owners already green: Storage Core 20/20, Local 31/31, Media Core 562/562, and Media Web
  8/8 (621 total);
- the six exact `0.20.0` artifacts pack with bounded `0.20.x` Koan dependency bands; Media Core retains
  its declared ImageSharp/Skia native asset closure;
- a clean external consumer restored only those six staged packages into a fresh cache, built with
  zero warnings/errors, and passed `AddKoan()`, Local storage, Media pipeline, and Media Web activation
  with `STORAGE-MEDIA|PACKAGE-CONSUMER|ADDKOAN|LOCAL|MEDIA-PIPELINE|MEDIA-WEB|PASS`;
- generated product truth is current at 42 claims / 93 packages; API posture is 67/73 configured,
  with exactly these six allowed first-publication floors pending and three content-only owners;
- lean no-tests coherence passed tool restore, solution build, composition lockfile, documentation
  truth/lint, diff-scoped code validation, skills lint, and blueprint lint; no test or container ran;
- publication, public indexing, and the unchanged NuGet.org-only consumer remain.
