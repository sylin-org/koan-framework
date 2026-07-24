---
type: ADR
domain: data
title: "DATA-0109 - Adapter-neutral polymorphic Entity roots"
audience: [architects, maintainers, developers, ai-agents]
status: accepted
last_updated: 2026-07-24
framework_version: source-first
---

# DATA-0109 — Adapter-neutral polymorphic Entity roots

## Context

Applications need one searchable set whose records have a shared shape and a small number of richer variants:

```csharp
public class Media : Entity<Media>
{
    public string Kind { get; set; } = "";
}

public sealed class Anime : Media<Anime>
{
    public int? Episodes { get; set; }
}
```

Filtering, sorting, counting, and paging over shared fields must execute once against the Media repository and remain
provider-pushed. Point access must remain ordinary Entity language: `await Anime.Get(id)` returns `Anime`, without a
cast or a generic argument at the call site.

The former shape guard rejected concrete inheritance because inferred writes selected `Data<Anime>` while inherited
statics selected `Data<Media>`. JSON-oriented adapters already retained runtime properties on write, but nominal
`Media` reads discarded the runtime type. Provider-native discriminator behavior differed and Mongo `_t` metadata is
explicitly outside Koan's storage contract.

## Decision

An Entity family has one **root type**, one physical repository, and any number of explicitly self-closed variants:

```csharp
public class Media : Entity<Media> { }
public sealed class Anime : Media<Anime> { }
public sealed class Manga : Media<Manga> { }

Anime? anime = await Anime.Get(id);
Media? media = await Media.Get(id);
var page = await Media.Page(1, 25);
```

Koan's transitive source generator emits the same-name, different-arity companion `Media<TVariant> : Media` for an
eligible root. The companion carries a root/variant/key marker and hides the complete point-`Get` overload family with
exact `TVariant` results. The marker is public framework infrastructure hidden from ordinary IntelliSense.
This is additive to the existing `Entity<Media>` ABI. The user declares no attribute, converter, provider option, or
call-site type argument.

`DataService` is the root-selection authority. A request for `IDataRepository<Anime, string>` produces an
adapter-neutral typed façade over the cached `IDataRepository<Media, string>` semantic façade. Adapter selection,
source routing, storage naming, schema, indexes, segmentation, field transforms, lifecycle, and cache policy are
compiled only for Media. No physical adapter is constructed for Anime.

Set-wide operations belong to the root. `Media.All`, `Query`, `Count`, `Page`, and streams operate over the complete
family and preserve provider pushdown. Variant façades provide point reads and writes needed by inferred APIs such as
`anime.Save()` and typed bulk saves; a direct variant set-query receives a corrective failure directing the caller to
the root.

## Runtime type contract

Koan owns one reserved source-type field, `__koan_type`. Once a root has variants, every family record—including a
plain root record—carries its source identity. This prevents an ambient typed point-read target from leaking onto a
nested root or sibling record. Its stable value is an assembly-simple-name plus full CLR type name, without assembly
version, culture, or public key token.

Resolution is restricted to a boot/discovery-compiled catalog of concrete Entity variants:

1. A stored identifier supplies the authoritative runtime type for that document.
2. For a legacy document without an identifier, a typed variant point operation supplies its requested target.
3. Otherwise, a missing identifier materializes the root.

A typed variant read may use its target for a legacy row with no identifier when the adapter materializes that row
inside the point-read operation. Eager stores that hydrate a complete file before the point read cannot retroactively
classify such rows. A stored sibling identity is materialized faithfully and then rejected by the typed variant façade,
which keeps nested members of the same family free to restore their own runtime types. An identifier that is unknown,
ambiguous, malformed, or belongs to another root fails closed. Koan never enables unrestricted Json.NET
`TypeNameHandling`, calls `Type.GetType` on stored input, or infers CLR type from a domain field such as `Kind`.

JSON-based stores and cache consume one Data Core contract resolver/materializer. Relational adapters acquire it
through their existing shared JSON-settings seam. Couchbase installs the same policy in its cluster serializer.
Mongo uses a thin Koan-owned BSON discriminator convention that maps `__koan_type` through the same catalog; Mongo
`_t` and provider-native type-name resolution remain disabled.

The descriptor, generated family facts, token maps, serializer delegates, and variant façades are cached. A
non-polymorphic Entity retains the direct repository and nominal-serialization fast path.

## Validation and correction

- A variant must close its family with itself; malformed shapes such as `Anime : Media<Manga>` fail.
- Root and variant key types must agree.
- A stored runtime type must be concrete, catalogued, and assignable to the root.
- Public properties, serializer mappings, extension data, framework-managed fields, and hard-segmentation fields may
  not collide case-insensitively with `__koan_type`.
- Eligible top-level, non-sealed class roots receive companions. If an arity-one type with the same name already
  exists, it wins and Koan emits nothing; ineligible roots and incompatible collisions remain ordinary C# binding or
  constraint errors.
- Direct concrete inheritance without the family companion receives a runtime correction to use `Media<Anime>`.

## Coalescence and ergonomics

`DataService` plus `RepositoryFacade` remains the single semantic/physical activation boundary. Root detection moves
out of the former rejection-only shape guard into one immutable descriptor. JSON source-type handling is absorbed by
the existing managed-field serialization bridge. Adapter-specific code only translates the common representation at
the wire boundary.

The application declares the root decision once:

```csharp
public sealed class Anime : Media<Anime>
```

IntelliSense then reports `Anime.Get` as `Task<Anime?>`; everyday code contains no conversion, generic operation, or
repository concept. Root set operations remain visibly rooted at `Media`.

## Consequences

- Anime, Manga, and Media share one table/collection and one root query plan on every adapter.
- Loading through Media and resaving preserves derived fields because runtime types survive materialization.
- Existing discriminator-free root rows remain compatible. Existing discriminator-free derived JSON cannot be
  classified safely by a mixed root read. An on-demand adapter can recover a known row through a typed read; an eager
  store requires migration/backfill.
- CLR type or assembly renames require an explicit data migration or a future centrally governed alias mechanism.
- Derived-only fields are payload by default. Portable filtering, indexes, segmentation, lifecycle, and set-wide policy
  remain root-owned; applications place shared searchable/policy-bearing members on the root. Record-local write stamps
  and field transforms resolve their memoized plan from the materialized runtime type, so a leaf `[Timestamp]` or
  `[Classified]` member is not silently skipped.
- `Anime.All` is inherited root-set syntax and therefore denotes the Media set. Documentation favors `Media.All` so
  set ownership remains legible.
- Mongo's process-global convention registration and generated-companion edge shapes require focused regression and
  AOT tests.
