---
type: GUIDE
domain: web
audience: [developers]
status: current
last_updated: 2026-09-10
framework_version: v1.0.0
validation:
  date_last_tested: 2026-07-18
  status: reviewed
  scope: front matter added; prose not re-verified
---

# OpenGraph social cards

Give a Koan SPA per-route social cards (and a per-route `<title>` and canonical link)
without server-side rendering. Social crawlers do not run JavaScript, so a SPA that
serves one static shell on every route unfurls every shared link as the same generic
card. This pillar injects the right `<head>` block into the shell on the crawler path,
keyed off your entities.

See the decision record at [WEB-0070](../decisions/WEB-0070-opengraph-social-cards.md)
for the rationale.

## 1. Reference the package

Add a reference to `Sylin.Koan.Web.OpenGraph`. Reference = availability: the package contributes its Web pipeline
behavior through the application's existing `AddKoan()` composition. It remains inert until a shell is configured;
Entity-specific metadata additionally requires a card declaration.

## 2. Register a card per entity type

Inside `AddKoan(() => ...)`, or in `KoanModule.Register` for a reusable application capability, declare one card per
type. Each `For<T>` supplies the three facts the framework cannot infer: the route
template (your SPA router owns it), the resolver (token to entity), and the
projections (entity to card fields).

```csharp
using Koan.Web.OpenGraph;

builder.Services.AddKoan(() => SocialCards
    .For<Work>("/work/{id}/{slug}", id => Work.Get(id))
        .Title(w => w.Name)
        .Description(w => w.Summary)
        .Image(w => CardImage.Recipe("share-card", w.CoverMediaId))
        .Url(w => $"/work/{w.Id}")
        .Type("article")
    .For<Article>("/articles/{id}/{slug}", id => Article.Get(id))
        .Title(a => a.Title)
        .Description(a => a.Excerpt)
        .Image(a => CardImage.Recipe("share-card", a.CoverMediaId)));
```

The primary token is the first route parameter (the entity id); the resolver is
uniformly `T.Get(id)`. Any further segments (the decorative SEO `{slug}`) are matched
and discarded, so `/work/{id}/{slug}`, `/work/{id}`, and a request like
`/work/{id}/anything` all resolve off the same id. The entity type must be a `Koan`
entity (`T : Entity<T>`).

Card registrations belong to the composing host. The registry retains the resolver and selectors for the request path,
so they should not close over a service provider, scope, runtime service, options snapshot, adapter, or disposable. Use `id => T.Get(id)` so Entity resolution selects the active Koan
runtime when the resolver executes, and keep selectors pure over the entity value. A second host receives a separate
registry and may independently declare the same Entity type; no reset API is necessary.

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

A null or empty `mediaId` collapses to the default. The middleware promotes the path to an absolute
URL rooted at the application's base URL: `Koan:Web:ApplicationUrl` when it is set (path included,
WEB-0078), else the URL the application accepted (WEB-0081), else the request's scheme, host and path base. The same root is used for `og:url` and the
canonical link.

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

## 4. Keep the application fallback

The package contributes its middleware at Koan's post-authorization Web pipeline boundary. The
application continues to own its normal static-file and SPA fallback behavior.

On a `GET` navigation (Accept includes `text/html`) the middleware injects the card
and short-circuits. Asset paths, `/api` paths, non-html requests, and the disabled
state all pass through to your fallback untouched.

A request routed to a controller action always passes through, so a controller's own responses,
errors, and 404s keep their status and body. Routing selects endpoints before this middleware runs,
so injection applies only to navigations no controller route claimed. The fallback remains
authoritative whenever OpenGraph passes through.

A card shows what its reader may read. The resolver runs on every navigation, and returning null from it shows no card;
the Entity's `[Access]` read gate and `EntityAccess<T>` read constraint then decide for the request's reader, as
`GET {id}` does. A crawler is an anonymous reader, so a private row unfurls as the default card:

```csharp
SocialCards.For<Article>("/articles/{id}", async id =>
        await Article.Get(id) is { Published: true } article ? article : null)
    .Title(a => a.Title);
```

A selector still chooses what the card says, so keep its fields suited to everyone who may read the row.

## A server-rendered page

A page a controller renders has no shell to inject into and needs no `ShellPath`. It has already read what its card says
and decided who may read it, so it hands the card to `IOpenGraphCardRenderer` and places the block in its own head:

```csharp
var head = renderer.RenderHead(new SocialCard(
    Title: topic.Title,
    Description: firstWords,
    Image: CardImage.Recipe("share-card", topic.CoverMediaId),
    Url: address,
    Type: "article",
    SiteName: place.Name), Request);
```

Each value is optional and takes its configured default when left out; a card with no title takes its site name. The block
is encoded, truncated and absolutized as a declared card's is. Nothing is read to render it, so nothing is checked either:
hand a card only where everyone who follows the link may read what it says. A page that draws its own `<title>` sets
`EmitTitleElement` to false. See [WEB-0085](../decisions/WEB-0085-a-server-rendered-page-hands-its-card.md).

## How it behaves

- Matched route, known entity: the entity's `<title>`, canonical, og: and twitter:
  tags are injected.
- Unknown id or slug, or a row this reader may not read: the default card (site name,
  default image, default description) is injected. Never an error.
- Unmatched route: the default card.
- Every emitted value is HTML-encoded; title and description are truncated to their
  configured maxima.

## Caching

A card is not stored. Each navigation reads the Entity for its reader, so a card follows
its Entity at once, whichever token routes it, and goes when the Entity or its
visibility does. Mark an Entity `[Cacheable]` to serve those reads from the cache pillar.
A card rendered for a signed-in reader carries `Cache-Control: private`.

## What this is not

No prerender, no headless browser, no user-agent sniffing, no routing ownership. It
injects into one static shell, or hands a server-rendered page its head block. oEmbed, JSON-LD, and sitemap
generation are out of scope.
