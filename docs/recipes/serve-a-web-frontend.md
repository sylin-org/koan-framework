---
type: RECIPE
recipe: serve-a-web-frontend
title: "Give the application a web frontend"
domain: web
status: current
last_updated: 2026-08-22
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/serve-a-web-frontend.md
gets_you: "A real UI for your API - embedded static pages, a self-opening local executable, or a detached frontend project - with the asset strategy chosen deliberately instead of improvised."
works_if: "The application needs pixels in a browser: a tool page, an admin surface, a gallery, a dashboard."
costs: "Embedded static files cost nothing. A client build adds a node toolchain to every build and CI. CDN scripts add a runtime network dependency and third-party script running on your origin."
ingredients:
  - "one | the web layer this UI talks to | Sylin.Koan.Web"
  - "one | durable rows behind the API - without it every Entity call fails with \"Koan Data has no provider candidates. Reference a Data connector and call AddKoan().\" | any Koan Data connector (e.g. Sylin.Koan.Data.Connector.Sqlite)"
  - "optional | entity-owned files behind upload UIs | Sylin.Koan.Storage, Sylin.Koan.Media.Web"
---

# Give the application a web frontend

An API answers requests; a frontend asks them. Koan takes no position on frameworks, bundlers, or
CSS taste - it holds one line firmly: the UI lives close to the application unless the application
has outgrown that. Answer two questions in order and the topology decides itself.

## Two questions, in order

**Where does the UI live?**

| Posture | Reach for it when | Shape |
|---|---|---|
| Embedded static (default) | local tools, admin surfaces, galleries - most applications | `wwwroot/` inside the API project; same process, same origin |
| Self-serving executable | tools handed to non-developers | the posture above plus launch ergonomics: run the exe, the browser opens |
| Detached frontend project | deep routing, its own team or cadence, a build pipeline that is a product | separate project speaking to the API over HTTP |

**What feeds its assets?**

| Strategy | Reach for it when | The honest cost |
|---|---|---|
| Vanilla - hand HTML/CSS/JS | small UIs; modern CSS and ES modules go further than expected | none. Nothing to install, update, or audit |
| Vendored files - the library's built output copied under `wwwroot/lib/` | one or two beloved libraries, needed offline | you own updating them |
| CDN tags | networked applications that want a library with zero build | runtime network dependency; third-party script executing on your origin. Pin versions, add integrity attributes |
| Local build - npm/vite wired into the C# build | component complexity, TypeScript, design systems | node toolchain in every build and CI; a supply chain to audit |

**The cross-axis rule:** a CDN tag quietly cancels the self-serving executable. The moment assets
load from the network at runtime, double-clicking the exe at a cabin no longer works. Offline
tools vendor their libraries or go vanilla.

Heuristic: start vanilla; vendor when one library earns its keep; adopt a build when the UI
itself is a product.

## Posture: embedded static

This is the default and needs no wiring. When `KoanWebOptions.EnableStaticFiles` is set (it is,
by default) and a `wwwroot` folder exists, Koan Web applies default-files and static-file
middleware during startup - see `KoanWebStartupFilter`. Same origin means cookies, Server-Sent
Events, and `[Access]` authorization apply to UI calls with no CORS decision ever made.

SnapVault demonstrates the whole story with hand-authored multi-page HTML and design-token CSS -
no package.json anywhere: [samples/applications/SnapVault](../../samples/applications/SnapVault/).
GardenCoop and TaskGraph carry smaller versions of the same shape.

For a compiled SPA, keep `wwwroot` as the build output and let MSBuild drive the client build:

```xml
<Target Name="Client" BeforeTargets="Build">
  <Exec Command="npm ci && npm run build" WorkingDirectory="client" />
</Target>
```

Point the bundler at the API during development so same-origin holds there too - vite example:

```js
export default {
  build: { outDir: '../wwwroot', emptyOutDir: true },
  server: { proxy: { '/api': 'http://localhost:5000' } },
}
```

Commit either the built output or the sources-plus-target; pick one rule and keep it. The
application publishes as before - `wwwroot` rides along, including under NativeAOT.

## Posture: self-serving executable

Ordinary ASP.NET Core hosting - not a Koan feature, just useful plumbing around one:

```csharp
var app = builder.Build();

var uiUrl = app.Configuration["Ui:Url"] ?? "http://localhost:5000";
app.Lifetime.ApplicationStarted.Register(() =>
{
    // best effort: headless hosts have no shell to hand the URL to
    Process.Start(new ProcessStartInfo(uiUrl) { UseShellExecute = true });
});

await app.RunAsync();
```

Bind a predictable port for the shortcut (`--urls`, a launch profile, or configuration), publish
- single file and NativeAOT both work with embedded static assets - and the result is a whole web
application as one executable someone can double-click. Because assets are local, it works
offline; because it is the same process, `/health/ready` and the facts endpoint describe the
whole thing.

## Posture: detached frontend project

When the UI is a product - its own router depth, its own team, its own release cadence - move it
out. Keep the API the source of truth and choose one seam:

- development: the frontend dev server proxies `/api` to Kestrel (the vite snippet above), so
  credentials and origins stay simple;
- production: either configure CORS deliberately on the API or put both behind one reverse proxy
  and preserve same-origin.

Costs to accept openly: two deploy units, two pipelines, and a version skew window between them.
Take this posture for those reasons, not by default.

## Boundaries

- Koan ships no JavaScript framework, CSS system, or scaffold generator; every library choice
  stays yours.
- The browser-open snippet above is ordinary hosting code, not a Koan capability.
- Koan applies minimal security headers by default (`KoanWebOptions.EnableSecureHeaders`). Adding
  CDN scripts means reviewing those headers together with your script tags.
