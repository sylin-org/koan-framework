---
id: WEB-0091
slug: wherever-an-action-answers-get-it-answers-head
domain: Web
status: Accepted
date: 2026-11-10
title: Wherever an action answers GET it answers HEAD
related:
  - WEB-0089
  - WEB-0090
---

# WEB-0091: Wherever an action answers GET it answers HEAD

## Context

ASP.NET routes no `HEAD` to an `[HttpGet]` action, so an application declared `HEAD` by hand, route by route, and every route it missed
answered `HEAD` with `404` or `405` where the page exists. A link checker, an uptime monitor and a Webmention sender ask `HEAD` first.

**The business sentence:** whatever can be read can be asked about.

Nothing is declared: every read does it.

## Decision

1. **A route that takes `GET` and not `HEAD` takes `HEAD` too,** in its method constraint and its endpoint metadata, so the read runs as
   it does for `GET`, with the method still `HEAD`, and the server sends its status and headers and no body. Koan.Web's
   `GetAnswersHead` convention does it once, where the application's actions are composed.
2. **An action that declares `HEAD` at a route itself keeps it,** and the `GET` at that route takes none, where the two templates are
   spelled alike, parameter names and constraints included; for that case, and no further, no request matches twice.
3. **The health alias answers `HEAD` as it answers `GET`.**
4. **The OpenAPI document lists each read once, as its `GET`:** Koan.Web.OpenApi includes no `HEAD` description.

## Consequences

- An application's own `[HttpHead]` beside an `[HttpGet]` at the same route still answers, and is no longer needed; where it is a tool,
  `RouteTools` keeps the two descriptors one tool.
- A read whose answer is costly to compute computes it for `HEAD` as well, unless it declares a cheaper `HEAD` of its own.

## Coalescence

- **Owner:** one `IApplicationModelConvention` beside `EntityHooksAreNotActions`, registered by Koan.Web.
- **Kept:** `StorageMediaController`'s cheaper `HEAD`, and `MediaController`'s two `[HttpHead]` copies, whose specs run on a host
  without Koan.Web's composition.
- **Not built:** a `HEAD` for a route that takes any method, which already answers it.

## Evidence

- `HeadSpec` (Koan.Mcp.Verbs.Tests, real Kestrel): a declared entity read, a plain read and the health alias answer `HEAD` with their
  `GET`'s status and content type and an empty body; an action that declares its own `HEAD` keeps it; each read is one tool.
- `HeadOutOfTheDocumentSpec` (Koan.Web.OpenApi.Tests): the document lists `get` operations and no `head`.
