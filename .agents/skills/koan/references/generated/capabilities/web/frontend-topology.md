---
type: REFERENCE
domain: web
title: "Web frontend topology"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/web/frontend-topology.md
---

# Web frontend topology

Put a useful browser experience in front of the Entity API without accidentally changing the
application's offline, build, origin, or deployment guarantees.

## You need

| Piece | Package | Note |
|---|---|---|
| The API the UI calls | `Sylin.Koan.Web` | same-origin is the low-complexity default |
| Entity-owned uploads (optional) | `Sylin.Koan.Storage` · `Sylin.Koan.Media.Web` | only when the UI handles stored files or derivatives |
| JavaScript framework | none supplied | Koan does not choose one |

## The constraint box

> **The constraint:** Asset strategy and deployment topology are one decision. A runtime CDN
> dependency cancels an offline self-serving executable; a detached frontend adds a second deploy
> unit, pipeline, origin decision, and version-skew window.

## Choose the topology first

| Shape | Fits when | Asset posture |
|---|---|---|
| Embedded static | local tools, admin surfaces, galleries, most first applications | `wwwroot`, same process, same origin |
| Self-serving executable | a non-developer should run one file and see the UI | embedded assets only; browser launch is ordinary hosting code |
| Detached frontend | the UI has deep routing, its own team, cadence, or pipeline | proxy in development; deliberate CORS or same-origin proxy in production |

Then choose vanilla files, vendored libraries, pinned CDN assets, or a local client build according
to the topology. Do not let the asset choice silently reverse it.

## Leaves

- **Decision guide with working snippets:** [serve a web frontend](../../recipes/serve-a-web-frontend.md)
- **Runnable exemplar:**
  [SnapVault](https://github.com/sylin-org/koan-framework/blob/main/samples/applications/SnapVault/README.md)
- **Tested contract:** [Web reference](../../reference/web/index.md)
