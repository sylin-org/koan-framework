# S20.OpenGraph

The smallest app that demonstrates `Koan.Web.OpenGraph`: declarative per-route social
cards injected into a static SPA shell on the crawler path. See the how-to at
[docs/guides/opengraph-howto.md](../../docs/guides/opengraph-howto.md) and the decision
record at [WEB-0070](../../docs/decisions/WEB-0070-opengraph-social-cards.md).

## Run

```bash
dotnet run --project samples/S20.OpenGraph
```

Then, with the crawler's `Accept: text/html`:

```bash
# Matched route, known entity: the note's title and description are injected.
curl -s -H "Accept: text/html" http://localhost:5080/notes/welcome | grep og:

# A trailing slug is discarded and resolves off the id.
curl -s -H "Accept: text/html" http://localhost:5080/notes/welcome/share-me | grep og:

# Unknown id: the default card (no error).
curl -s -H "Accept: text/html" http://localhost:5080/notes/unknown | grep og:

# Unmatched route and the shell itself: the default card.
curl -s -H "Accept: text/html" http://localhost:5080/ | grep og:
```

The port may differ; check the startup log. This sample uses the in-memory data
connector and seeds one note (`welcome`) at startup. `og:image` points at a Koan.Media
recipe URL (`/media/{id}/share-card`); wire up `Koan.Media` and a `share-card`
`[MediaRecipe]` to serve a real image, or set `DefaultImage` for a static fallback.
