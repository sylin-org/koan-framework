---
type: RECIPE
recipe: accept-and-serve-files
title: "Let people upload files and serve them back"
domain: media
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/accept-and-serve-files.md
gets_you: "Files owned by an Entity, stored once, and served back — including resized or converted versions."
works_if: "Users need to send you a file, or the application produces one worth keeping."
costs: "The local path needs disk you must back up. Derivatives cost CPU on first request or on ingest."
ingredients:
  - "one | Entity-owned files | Sylin.Koan.Storage"
  - "one | somewhere to put the bytes, user's choice | Sylin.Koan.Storage.Connector.Local, Sylin.Koan.Storage.Connector.S3"
  - "optional | recipes and derivatives over HTTP | Sylin.Koan.Media.Core, Sylin.Koan.Media.Web"
  - "optional | durable ingest and processing | Sylin.Koan.Jobs"
---

# Let people upload files and serve them back

Storage holds bytes an Entity owns. Media adds named recipes — a thumbnail, a converted format — as
derivatives of an original that is never overwritten.

## When this is the answer

"Users upload a profile photo." "Attach a PDF to the invoice." "We need thumbnails."

Two separations decide the design, and both are easy to get wrong:

- **Storage versus media.** If files only need to go in and come back out unchanged, storage alone is
  the answer and media is unnecessary machinery. Add media when *derived* versions are wanted.
- **Original versus derivative.** The original is the record; derivatives are reproducible. Never let a
  transform overwrite the thing a user sent you.

Then the questions that actually change the build:

- **How large, and how often?** Size bounds are a security control, not a nicety. Decide them now.
- **Who may fetch it?** A file behind an unguessable URL is public. If access matters, delivery must be
  governed like any other read.
- **Derive on upload or on first request?** Ingest cost versus first-view latency — and on-upload work
  belongs in [background work](run-work-in-background.md).
- **How long do you keep it?** Retention and deletion are policy, and deleting an Entity does not by
  itself dispose of what it owned.

The local connector is the honest default. The S3 connector is **not assessed** and its own README
calls it shelved — prefer the local path and say so plainly.

## Assembly

```powershell
dotnet add package Sylin.Koan.Storage
dotnet add package Sylin.Koan.Storage.Connector.Local
```

Storage needs a connector; without one it has nowhere to put anything. Media brings Storage
transitively, so adding media does *not* remove the need to choose where bytes live.

Configure one profile and the provider's physical settings. A sole profile is the implicit default.
An Entity that owns files derives from `MediaEntity<T>` and names where its bytes belong:

```csharp
[StorageBinding(Profile = "cold", Container = "photos")]
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    public string OriginalFileName { get; set; } = "";
}
```

Named derivatives are declared once, discovered automatically, and served by `Koan.Media.Web` at
`GET /media/{id}/{name}` — the seedless route returns the original:

```csharp
[MediaRecipe("masonry", Description = "300px masonry grid tile, JPEG")]
public static MediaRecipe Masonry() => MediaRecipe.New().ResizeFit(300, 300).EncodeAs("jpeg");
```

Recipe names are global slugs and must avoid the reserved format shortcuts (`jpeg`, `png`, `webp`,
`gif`). The engine auto-orients by default, so no orient step is needed.

Depth: [media recipes how-to](../guides/media-recipes-howto.md).

## Prove it

1. **Behavior** — upload, retrieve, and if media is in play, request a derivative and get the expected
   variant.
2. **Composition** — assert the storage profile and provider you intended are the ones in use.
3. **Correction** — an oversized upload, an unsupported type, and a failed transform each fail
   explicitly. Assert an unauthorized fetch is refused, not merely undiscoverable.

## Boundaries

- Koan does not provision a bucket, back up your disk, or scan uploads for malware.
- A derivative is not a new original, and re-deriving must be safe.
- Public delivery is a decision, never a default to drift into.

## Interacts with

**Tenancy.** File paths and delivery URLs must be tenant-scoped. Predictable keys across tenants are a
cross-customer read waiting to happen.

**AI.** Describing or reading uploaded images is [read an image](read-an-image.md); this recipe gives
those files somewhere to live.
