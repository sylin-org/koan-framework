# OpenGraph social cards (Koan.Web.OpenGraph)

Give a Koan SPA per-route social cards (and a per-route `<title>` and canonical link)
without server-side rendering. Social crawlers do not run JavaScript, so a SPA that
serves one static shell on every route unfurls every shared link as the same generic
card. This pillar injects the right `<head>` block into the shell on the crawler path,
keyed off your entities.

See the decision record at [WEB-0070](../decisions/WEB-0070-opengraph-social-cards.md)
for the rationale.

## 1. Reference the package

Add a reference to `Sylin.Koan.Web.OpenGraph`. Reference = Intent: nothing activates
until you register a card and call `UseOpenGraphCards()`.

## 2. Register a card per entity type

In your application's `Program.cs`, or in `KoanModule.Register` for a reusable package, declare one card per
type. Each `For<T>` supplies the three facts the framework cannot infer: the route
template (your SPA router owns it), the resolver (token to entity), and the
projections (entity to card fields).

```csharp
using Koan.Web.OpenGraph;

SocialCards
    .For<Work>("/work/{id}/{slug}", id => Work.Get(id))
        .Title(w => w.Name)
        .Description(w => w.Summary)
        .Image(w => CardImage.Recipe("share-card", w.CoverMediaId))
        .Url(w => $"/work/{w.Id}")
        .Type("article")
    .For<Article>("/articles/{id}/{slug}", id => Article.Get(id))
        .Title(a => a.Title)
        .Description(a => a.Excerpt)
        .Image(a => CardImage.Recipe("share-card", a.CoverMediaId));
```

The primary token is the first route parameter (the entity id); the resolver is
uniformly `T.Get(id)`. Any further segments (the decorative SEO `{slug}`) are matched
and discarded, so `/work/{id}/{slug}`, `/work/{id}`, and a request like
`/work/{id}/anything` all resolve off the same id. The entity type must be a `Koan`
entity (`T : Entity<T>`).

Card registrations are process-stable declarations. The registry retains the resolver and selectors
for request-time cold fill and lifecycle warming, so they must not close over a service provider,
scope, runtime service, options snapshot, adapter, or disposable. Use `id => T.Get(id)` so Entity
resolution selects the active Koan runtime when the resolver executes, and keep selectors pure over
the entity value. `SocialCards.Reset()` exists for test isolation; it is not a per-host production
registration model.

Because the selectors are pure functions over the loaded entity, per-entity-state
customization is a one-line conditional, which an attribute scheme could not express:

```csharp
.Image(w => w.Images.Count >= 2
    ? CardImage.Recipe("share-card-duo", w.Images[0].MediaId)
    : CardImage.Recipe("share-card", w.CoverMediaId))
```

### CardImage

`CardImage` composes an `og:image` URL; it renders nothing.

| Factory | Resolves to |
| --- | --- |
| `CardImage.Recipe("share-card", mediaId)` | `/media/{mediaId}/share-card` |
| `CardImage.Raw(mediaId)` | `/media/{mediaId}` |
| `CardImage.Url("https://...")` | the explicit URL |
| `CardImage.Default` | `OpenGraphOptions.DefaultImage` |

A null or empty `mediaId` collapses to the default. The middleware promotes the path
to an absolute URL using the request scheme and host.

## 3. Configure `Koan:Web:OpenGraph`

```jsonc
{
  "Koan": {
    "Web": {
      "OpenGraph": {
        "ShellPath": "wwwroot/index.html",
        "SiteName": "My Site",
        "DefaultImage": "/img/social-default.png",
        "DefaultDescription": "...",
        // Optional. Defaults shown.
        "PlaceholderMarker": "<!--KOAN_OPENGRAPH-->",
        "DefaultType": "website",
        "TwitterCard": "summary_large_image",
        "EmitTitleElement": true,
        "EmitCanonical": true,
        "EmitTwitterTags": true,
        "MaxTitleLength": 70,
        "MaxDescriptionLength": 200
      }
    }
  }
}
```

`ShellPath` points at the SPA shell on disk. Place the marker in the shell where the
block should land; if the marker is absent, the block is inserted before `</head>`.

```html
<head>
  <!--KOAN_OPENGRAPH-->
</head>
```

## 4. Insert the middleware ahead of your fallback

```csharp
// Program.cs, after UseStaticFiles, before MapFallbackToFile:
app.UseOpenGraphCards();
app.MapFallbackToFile("index.html");
```

On a `GET` navigation (Accept includes `text/html`) the middleware injects the card
and short-circuits. Asset paths, `/api` paths, non-html requests, and the disabled
state all pass through to your fallback untouched.

Note on the root path: if a default-document middleware (default files / SPA static
serving) sits ahead of `UseOpenGraphCards`, it may serve `index.html` for `/` before
this middleware runs, so `/` receives the raw shell rather than the default card. Deep
entity links (the shareable URLs) are unaffected. Place `UseOpenGraphCards` ahead of
that middleware, or accept that the bare root serves the default shell.

## How it behaves

- Matched route, known entity: the entity's `<title>`, canonical, og: and twitter:
  tags are injected.
- Unknown id or slug: the default card (site name, default image, default
  description) is injected. Never an error.
- Unmatched route: the default card.
- Every emitted value is HTML-encoded; title and description are truncated to their
  configured maxima.

## Caching

Each card is materialized as a `SocialCardSnapshot` entity marked `[Cacheable]`, so it
reuses the cache pillar (L1/L2 plus coherence) when a cache adapter is referenced, and
falls back to a plain persisted read otherwise. The snapshot is kept fresh by entity
entity Lifecycle: it is rebuilt when the source entity is saved and removed when the
source entity is deleted. The request path reads the snapshot and lazily fills it on a
miss, so the common case is a cache read plus a string replace.

## What this is not

No SSR or prerender, no headless browser, no user-agent sniffing, no routing
ownership. It is string injection into one static shell. oEmbed, JSON-LD, and sitemap
generation are out of scope.
