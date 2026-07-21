---
id: WEB-0070
slug: WEB-0070-opengraph-social-cards
domain: WEB
status: Accepted
date: 2026-06-13
---

# ADR 0070: Koan.Web.OpenGraph, declarative social cards for SPA shells

Date: 2026-06-13

Status: Accepted

## Context

A Koan-native SPA serves one static `index.html` on every route through
`MapFallbackToFile`. That shell carries no per-route `og:` / `twitter:` tags, and
social crawlers (Discord, Slack, Facebook, Twitter/X, LinkedIn, iMessage) do not
execute JavaScript: they read the initial HTML and stop. So every shared link
unfurls as the same generic card, regardless of what it points at. The same is
true of search-engine first-paint title and description.

The expensive half of "social cards" is already a framework capability: the card
image is a Koan.Media recipe served at `GET /media/{id}/{seed}`, entity resolution
is `Entity<T>.Get(id)`, and there are two existing registry idioms to clone
(`Entity<T>.Events`, `Koan.Data.Vector` `VectorProfiles`). The only genuinely new
mechanics are contributing the head vocabulary, owning its correctness once
(HTML-encoding and length truncation), and injecting it into the shell on the
crawler path.

## Decision

Ship a new web pillar, `Koan.Web.OpenGraph` (package `Sylin.Koan.Web.OpenGraph`),
with the following shape.

### 1. A fluent, type-keyed registry, not an entity interface or CRTP base

`SocialCards.For<T>(routeTemplate, resolve)` returns a `SocialCardBuilder<T>` whose
pure selectors project the card fields, and which chains across types via `For<U>`.
Registration writes into a static, type-keyed registry (the `Entity<T>.Events`
mechanic) read at request time.

A domain entity is not "a card"; being shareable is one aspect of it. So the
registry attaches the aspect beside the entity rather than consuming its identity.
An `IKoanWebCard` interface implemented on the entity would be wrong: it couples a
domain data root to a presentation concern, and it conflicts with EntityShapeGuard
(the type is already its own `Entity<T>` root and cannot adopt a second concrete
root). The delegate form is also strictly more expressive than an attribute scheme:
a selector can inspect the whole entity (for example, choosing a different image
when an entity has two or more images), which an attribute cannot express.

### 2. The card cache is a sidecar `[Cacheable]` entity, not bespoke machinery

The projected card is modelled as its own tiny entity, `SocialCardSnapshot`, marked
`[Cacheable]`. This reuses the entity-cache pillar wholesale: `SocialCardSnapshot.Get(key)`
inherits L1/L2 caching and cross-node coherence when a cache adapter is referenced,
and degrades to a plain persisted read when one is not (Reference = Intent). The
pillar writes no cache machinery of its own.

The only out-of-band wiring is an `Entity<T>.Events` hook, registered when a card is
declared, that keeps the snapshot in sync with its source entity: on upsert it
projects the in-hand entity and saves the snapshot (warm, one write and no read); on
remove it deletes the snapshot key. The request path reads the snapshot first and
lazily fills it on a miss (cold, evicted, or an entity that predates registration),
so the resolver remains the cold-fill path rather than becoming redundant.

The deliberate trade is that this materializes a small table or collection in the
consumer's store. That is the cost of persistence-and-coherence-for-free; it follows
the `Koan.Jobs` precedent (`JobRecord`, `JobMetric`, `JobGateRecord` are pillar-owned
entities persisted into the consumer's store) and is routable to any provider,
including an in-memory set for an ephemeral cache. The alternative, a hand-rolled
type-keyed dictionary with manual coherence, would duplicate machinery the cache
pillar already provides and forfeit free multi-node invalidation.

### 3. No Koan.Media dependency

`CardImage` only composes a URL (`/media/{id}/{recipe}`, `/media/{id}`, an explicit
URL, or the configured default). The pillar renders no images: no Koan.Media project
reference, no ImageSharp, no headless browser. A missing or failed recipe is handled
by the existing `MediaController`, which falls back to original bytes, so a missing
overlay never produces a broken card.

### 4. A middleware the consumer inserts ahead of its own fallback

The app, not Koan, owns `MapFallbackToFile`. So `UseOpenGraphCards()` is middleware
the consumer places one line ahead of its fallback, and `IOpenGraphCardRenderer` is
the lower-level seam for apps that wire the render into a custom endpoint. The pillar
does not take ownership of the host request pipeline. With no cards registered and no
middleware call, the pillar is inert.

### 5. The seam is per-route head injection; OpenGraph is its first vocabulary

The same resolve / cache / encode / inject pipeline also serves the organic-search
surface, so the pillar emits the `<title>` element and `<link rel="canonical">` from
the same resolved values (both behind options toggles, both on by default). Canonical
in particular is one line off the absolutized `og:url` and prevents duplicate-content
splitting across the discarded-slug route shape. oEmbed, JSON-LD, and sitemap
generation remain out of scope, but the `SocialCardSnapshot` rows are a concrete-URL
list that a future sitemap generator could enumerate directly, and the internals are
named so that further head vocabulary stays additive rather than a rewrite.

## Consequences

Positive:
- A consumer registers a card in a few lines and gets per-route social cards plus
  basic SEO (title, canonical) for free.
- Caching, coherence, and persistence are inherited from existing pillars; the
  pillar owns no cache code.
- HTML-encoding and length truncation are owned once, so apps cannot get them wrong.

Negative / risks:
- A small persisted table per carded type appears in the consumer's store.
- The request path acts on every HTML navigation that falls through to the fallback,
  not just crawler requests (no user-agent sniffing by design); the cost on a hit is
  one snapshot read plus an in-memory string replace.
- Truncation is applied at emit time over a hard-capped stored value, so changing a
  configured maximum takes effect on the next re-projection.

## References

- Proposal: `docs/proposals/koan-web-opengraph.md` (in the first consumer's repo).
- Cloned idioms: `Koan.Data.Core` `Entity<T>.Events`, `Koan.Data.Vector` `VectorProfiles`.
- Cache pillar: ARCH-0075, ARCH-0078 (`[Cacheable]`, `LayeredCache`, coherence).
- Entity-owning-pillar precedent: JOBS-0005 (`JobRecord`).
- Integration-test canon: ARCH-0079 (`KoanIntegrationHost`).
- How-to: `docs/guides/opengraph-howto.md`.
