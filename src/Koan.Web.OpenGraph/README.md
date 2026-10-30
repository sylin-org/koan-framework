# Sylin.Koan.Web.OpenGraph

Project Entity-owned social metadata into a SPA shell for link previews and HTML navigations—without SSR, user-agent
sniffing, or an application middleware call.

```bash
dotnet add package Sylin.Koan.Web.OpenGraph
```

## Smallest meaningful result

Configure the shell, then declare the route-to-Entity projection while Koan composes the application:

```json
{
  "Koan": {
    "Web": {
      "OpenGraph": {
        "ShellPath": "wwwroot/index.html",
        "SiteName": "My Site"
      }
    }
  }
}
```

```csharp
builder.Services.AddKoan(() =>
    SocialCards.For<Article>("/articles/{id}", id => Article.Get(id))
        .Title(article => article.Title)
        .Description(article => article.Summary)
        .Url(article => $"/articles/{article.Id}")
        .Type("article"));
```

The package contributes its Web pipeline behavior automatically. A matching HTML navigation receives encoded title,
canonical, OpenGraph, and Twitter metadata. Unknown entities and unmatched routes receive configured defaults; asset,
API, non-HTML, disabled, and unavailable-shell requests pass through.

Each declaration belongs to the composing host. Resolvers should use Entity statics and selectors should remain pure
over the Entity value; application services are not captured.

A link preview shows what its reader may read. The resolver runs on every navigation and may return null to show no
card; the Entity's `[Access]` read gate and `EntityAccess<T>` read constraint then decide for the request's reader, as
`GET {id}` does. A crawler is an anonymous reader. Nothing is stored, so a card follows its Entity at once, whichever
token routes it. An Entity read often enough to matter is marked `[Cacheable]` as any other is.

## Image projection

`CardImage.Recipe("share-card", mediaId)` projects `/media/{id}/share-card`, `CardImage.Raw(mediaId)` projects the
original, and `CardImage.Url(...)` preserves an external URL. This package composes URLs only; Media owns rendering.

## A server-rendered page

A page drawn by a controller has no shell. It hands its card over and places the block in its own head; no `ShellPath` is
needed, and what the card says and who may see it are the page's:

```csharp
var head = renderer.RenderHead(new SocialCard(topic.Title, firstWords, Url: address, Type: "article", SiteName: place.Name), Request);
```

## Boundaries

- The application owns its SPA fallback and shell file.
- `SocialCards.For<T>` must run inside `AddKoan(() => ...)` or a `KoanModule.Register` method.
- One host may declare one card per Entity type; separate hosts are isolated.
- A card rendered for a signed-in reader carries `Cache-Control: private`; no CDN purge or distributed shell cache is
  claimed.
- No rendering of the page itself, prerender, headless browser, oEmbed, JSON-LD, sitemap, or routing ownership is provided.

See the [OpenGraph guide](../../docs/guides/opengraph-howto.md) and [technical contract](TECHNICAL.md).

Automatic injection runs at the post-authorization Web pipeline boundary and only for navigations no
controller route claimed. Controller endpoints retain their response and authorization semantics: a
controller result, including its errors and 404s, passes through untouched. Controllers may call
`IOpenGraphCardRenderer.RenderShellAsync(Request, ct)` after their business visibility checks; SPA
fallback navigation remains automatic. A card is read for the request's reader on every navigation: the resolver, then the Entity's read gate and
read constraint, the read `GET {id}` answers. A token nothing resolves and a row the reader may not read both receive
the default card. A selector still chooses what the card says, so keep its fields suited to everyone who may read the
row; a property-level `[Access]` does not reach a selector.
