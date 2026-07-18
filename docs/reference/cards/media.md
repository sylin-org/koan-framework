---
type: REFERENCE
domain: media
title: "Media Capability Card"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-18
framework_version: v0.18.0
validation:
  date_last_tested: 2026-07-18
  status: verified
  scope: shortest path, routes, inspection, and unsupported lifecycle behavior
---

# Media capability card

## Use it when

Your application has a stored media Entity and wants named, inspectable image transforms without owning a
rendering controller or transform-registration layer.

## Smallest useful shape

```csharp
public sealed class Photo : MediaEntity<Photo> { }

[MediaRecipe("card")]
public static MediaRecipe Card() => MediaRecipe.New()
    .Resize(width: 320)
    .EncodeAs("jpeg", Quality.Web)
    .Build();

builder.Services.AddKoan().AsWebApi();
```

Result: `GET /media/{id}/card`.

## What Koan owns

- code/config recipe discovery and startup validation;
- pipeline execution and output-format negotiation;
- source access through active Entity axes;
- optional persisted derivatives;
- HTTP caching and diagnostic headers; and
- startup/operator/agent facts for the materialized recipe catalog.

## Important limits

- no prewarm workflow or generic Media Entity facet;
- one automatically selected `IMediaSource` per bare route; multiple media Entity types require an explicit choice;
- default derivative writes buffer the rendered output and are best-effort;
- source deletion needs targeted derivative cleanup; and
- no signed/content-addressed route or configurable route prefix.

Full contract: [Media pillar reference](../media/index.md).
