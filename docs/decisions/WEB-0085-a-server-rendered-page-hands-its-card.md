---
id: WEB-0085
slug: a-server-rendered-page-hands-its-card
domain: Web
status: Accepted
date: 2026-10-30
title: A server-rendered page hands its card to the renderer and places the head block itself
amends:
  - WEB-0070
related:
  - WEB-0078
  - WEB-0083
---

# WEB-0085: A server-rendered page hands its card to the renderer and places the head block itself

## Context

`Koan.Web.OpenGraph` injects a card into one static shell for a navigation no controller claimed. A server-rendered
application has neither: every address is a controller action, which the middleware rightly leaves alone, and each page
draws its own head. Its first such consumer, Tangent Space, also could not declare its cards. The primary token is the
route's first parameter, so `/t/{tangent}/{topic}` resolves the Tangent and never sees the Topic; a catch-all is refused,
as is a route with no parameter such as `/`; and the site name comes only from configuration, where the place's own name
is a row its proprietor changes.

Such a page has already read what its card says and has already decided who may read it. What it lacks is what this
package owns once: encoding, truncation, absolutizing and the head's vocabulary.

## Decision

1. **`IOpenGraphCardRenderer.RenderHead(SocialCard, HttpRequest)` returns the head block.** Its title element where
   configured, canonical link, OpenGraph and Twitter tags, from the one rendering a declared card uses. It is empty when
   OpenGraph is disabled.

2. **`SocialCard` is public.** It carries title, description, image, URL, type and site name, each optional and each
   taking the configured default when left out; a card with no title takes its site name. A declared card projects the
   same record and leaves the site name to configuration.

3. **The seam reads nothing.** No shell file, no route match, no Entity read, no I/O. What a card says and who may see it
   are the caller's, which is why the page calls it after its own visibility decision.

Route declarations do not grow a multi-token form or a per-request site name: both would answer a question a
server-rendered page has already answered, with a second read.

## Consequences

- A server-rendered application references the package and needs no `ShellPath`; with none, the middleware passes through.
- The application places the block in its head and disables `EmitTitleElement` if its page already draws a title.
- The guarantee of WEB-0083, that a card shows what its reader may read, holds for declared cards by the Entity's read. For
  a handed card it is the application's, and the guide says so.

## Verification

`RenderHeadSpec` (Koan.Web.OpenGraph.Tests): the whole head with no shell configured, encoding and truncation, an absolute
URL kept and a missing one taken from the request, the configured defaults and a handed site name, the toggles and a
disabled pillar, and no resolver run on a path a declared card routes. These exercise surface that did not exist, so they
could not compile before the change.
