# Koan.Tagging

Tag system primitives for Koan-built applications.

## Overview

Two layers, deliberately separated:

- **`TagSet`** — a **model-facet** value type that lives on entities. Holds tags
  in two visibility scopes (`Public` / `Private`) each grouped into open-ended
  named categories. The `Public`/`Private` cut is a serialisation boundary, not
  just a naming convention.
- **`Tag`** — a **domain-entity** that manages the roster of canonical tag
  identities themselves. `Tag.ParentOf` is a synonym graph: aliases listed there
  resolve to the Tag's canonical `Id` at write time. No taxonomic hierarchy,
  no parent-walking — just rename-on-write canonicalisation.

## Quick start

```csharp
using Koan.Tagging;

var tags = new TagSet();

tags.Public["game"].Set(["ffxiv", "expedition-33"]);
tags.Public["technique"].Set("dof").Set("clarity").Set("magicbloom");
tags.Public["aesthetic"].Set("cinematic");
tags.Private["moderation"].Set("review-pending");

tags.Has("ffxiv");                              // true (defaults to Public)
tags.Has("review-pending");                     // false (Public default)
tags.Has("review-pending", TagSet.EScope.Private); // true
tags.Find("ffxiv");                             // TagLocation(Public, "game")

tags.PublicTags;
// ["ffxiv", "expedition-33", "dof", "clarity", "magicbloom", "cinematic"]
```

A typical entity:

```csharp
public sealed class Package : Entity<Package>
{
    public string? Name { get; set; }
    public TagSet Tags { get; set; } = new();
    // ...
}
```

A typical public-surface projection:

```csharp
public sealed class PublicPackage
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    public static implicit operator PublicPackage(Package p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Tags = p.Tags.PublicTags,  // <-- the Public/Private boundary
    };
}
```

## Synonym registry (Tag entity)

Use the `Tag` entity to declare canonical names and their aliases:

```csharp
await new Tag
{
    Id = "ffxiv",
    DisplayName = "Final Fantasy XIV",
    ParentOf = ["ff14", "final-fantasy-xiv", "final-fantasy-14"]
}.Upsert();

// At write time, normalise input:
string canonical = await ResolveCanonical("ff14");  // → "ffxiv"
tags.Public["game"].Set(canonical);

async Task<string> ResolveCanonical(string raw)
{
    var match = await Tag.Query(t => t.ParentOf.Contains(raw));
    return match.FirstOrDefault()?.Id ?? raw;
}
```

The Tag entity is opt-in: tag strings that don't have a corresponding Tag
entity are their own canonical form. Add a Tag entity only when
canonicalisation, admin-managed metadata, or a synonym story matters for that
tag.

## JSON shape

`TagSet` serialises as a nested object:

```json
{
  "public": {
    "game": ["ffxiv"],
    "technique": ["dof", "clarity"]
  },
  "private": {
    "moderation": ["review-pending"]
  }
}
```

Empty categories are stripped. The flat `PublicTags` projection is what public
surfaces typically emit instead.

## Design notes

- **Category names are open-ended.** Consuming projects document their own
  conventions (`game`, `source`, `technique`, `aesthetic`, etc.). Add new
  categories without code changes.
- **No hierarchy on `Tag`.** `ParentOf` is intentionally a synonym graph,
  not a taxonomy. If a downstream needs hierarchy, build it separately.
- **Tag entities are optional.** TagSets work on flat strings; the Tag
  registry adds canonicalisation only where it's earned.
- **`Public`/`Private` is a serialisation boundary.** Public surfaces consume
  `TagSet.PublicTags`; admin surfaces consume the full TagSet. The compiler
  enforces the boundary when projections are typed (e.g. `PublicPackage` vs.
  `Package`).
