# Sylin.Koan.Web.OpenGraph — technical contract

## Composition ownership

`OpenGraphModule` binds `Koan:Web:OpenGraph`, creates one `SocialCardRegistry` instance in the application's
`IServiceCollection`, registers the renderer/shell cache, and contributes `OpenGraphPipelineContributor` at
`AfterAuthorization`. There is no manual activation path.

`SocialCards.For<T>` requires `KoanCompositionScope` and adds its registration to that host's registry. Application
resolver/selector closures therefore release with the host and cannot leak into a later composition.

## Request flow

1. pass through unless the request is an HTML `GET` outside `/api` and known asset extensions; a
   request routed to a controller action always passes through, whatever status it produces;
2. load the configured shell through `ShellCache`;
3. match declarations in registration order and extract the route token;
4. run the application's resolver for the token;
5. read what it found through `IEntityEndpointService<T, string>.GetById` for the request's principal, so the Entity's
   read gate, read constraint and hooks decide, and project the card from that read;
6. encode and truncate configured/entity values; and
7. replace the marker or insert before `</head>`, then short-circuit the navigation, with `Cache-Control: private` when
   the reader is signed in.

A resolver's null, a read that refuses and a read that finds nothing all receive the default card, so a card says
nothing of what exists beyond the reader's sight. Nothing is persisted (WEB-0083): the Entity's own read, with its
`[Cacheable]` acceleration where it declares it, is the only copy, so a card cannot outlive its Entity or its
visibility. A failure in the resolver, the read or a selector surfaces on the request.

The read runs outside any partition the resolver opened for itself: a resolver that reads a named partition finds its
row absent from this read, and the reader receives the default card.

## Options

`Koan:Web:OpenGraph` owns enablement, `ShellPath`, `PlaceholderMarker`, site/default metadata, Twitter/title/canonical
toggles, locale, and title/description limits. The module reports effective enablement, shell posture, and declaration
count at startup.

## Runtime and hot path

Route declarations and lifecycle plans are compiled once per host. The request hot path iterates an immutable
copy-on-write registration list, runs one resolver and one keyed Entity read, and one shell string replacement.
`ShellCache` reloads when the shell file's timestamp changes.

## Unsupported

No routing/fallback ownership, SSR/prerender, user-agent policy, distributed shell cache, CDN invalidation, oEmbed,
JSON-LD, sitemap generation, or guarantee that external crawlers honor emitted metadata is claimed.

Automatic injection runs at the post-authorization Web pipeline boundary and only for navigations no
controller route claimed. Controller endpoints retain their response and authorization semantics: a
controller result, including its errors and 404s, passes through untouched. Controllers may call
`IOpenGraphCardRenderer.RenderShellAsync(Request, ct)` after their business visibility checks; SPA
fallback navigation remains automatic. A card is read for the request's reader on every navigation: the resolver, then the Entity's read gate and
read constraint, the read `GET {id}` answers. A token nothing resolves and a row the reader may not read both receive
the default card. A selector still chooses what the card says, so keep its fields suited to everyone who may read the
row; a property-level `[Access]` does not reach a selector.
