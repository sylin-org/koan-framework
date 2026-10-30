---
id: WEB-0083
slug: the-web-fails-closed
domain: Web
status: Accepted
date: 2026-10-30
title: The web fails closed where it read as open, and believes each declared proxy
amends:
  - WEB-0070
  - WEB-0047
  - WEB-0078
  - WEB-0081
related:
  - SEC-0002
  - SEC-0004
---

# WEB-0083: The web fails closed where it read as open, and believes each declared proxy

## Context

An audit of the Web hosting pillars found five failures with one shape: a default that served the wrong party, or a
failure nobody saw.

- `Koan.Web.OpenGraph` read a stored `SocialCardSnapshot` before the application's resolver, and every save of a carded
  Entity wrote one, private or not, so any anonymous navigation received the card of a private row. Snapshots were filled
  by route token and warmed and evicted by Entity id, so a card routed by a slug went stale forever and outlived its row.
- A subclass of `EntityModerationController` or `EntityAuditController` with no policy let any signed-in caller act. The
  policy provider was registered only by an application's `AddCapabilityAuthorization`, so without that call the seam's
  allow-by-default answered; with it, `DefaultBehavior` was `Allow`. A per-entity policy did not reach a concrete
  controller, whose own name was taken for the Entity's.
- `Koan:Web:TrustedProxies` was believed for exactly one hop, so behind a CDN and a reverse proxy every visitor read as
  the CDN's edge, and anything keyed by address treated them as one person.
- `KoanWebOrigins` evicted the least recently seen origin, and a request's Host is the caller's to write, so a scanner's
  random hosts pushed out the origin people arrive on and reset its count before an application could offer it.
- `KoanWebStartupFilter` discarded a failure to initialize the environment, and caught the error of a missing
  `AddAuthorization`, turning a clear boot error into a 500 on each request.

## Decision

1. **A link preview shows what its reader may read, read now.** Each navigation runs the resolver, then reads what it
   found through `IEntityEndpointService<T, string>.GetById` for the request's principal, so the Entity's read gate, read
   constraint and hooks decide exactly as `GET {id}` does. A crawler is an anonymous reader. A token nothing resolves and a
   row the reader may not read receive the same default card, saying nothing of what exists.

   `SocialCardSnapshot` and its lifecycle hooks are removed. A snapshot read before the visibility check is the leak, and a
   snapshot read after one saves nothing: the check is itself a read. The Entity's own `[Cacheable]` is the acceleration.
   A store's snapshot rows are unused; stores are rebuilt. A card rendered for a signed-in reader carries
   `Cache-Control: private`, because a response now differs by reader.

2. **A capability action with no policy is denied.** `WebExtensionsModule` registers `PolicyAuthorizationProvider` with
   the reference, so it decides every capability action whether or not the application called anything;
   `DefaultBehavior` is `Deny` everywhere, with no environment branch; and the 403 names the action, the Entity and what to
   map. `Allow` remains an application's explicit word. A controller's Entity is the first generic argument of its nearest
   closed base. `RequireCapabilityAttribute` requires `IAuthorize`, which `Koan.Web` always registers, and no longer
   treats its absence as permission.

3. **Every declared proxy is believed, and nothing else.** `ForwardLimit` is unset, with no hop count to configure. The
   walk starts at the connection, steps back one forwarded entry for each declared proxy it passes, and stops at the first
   address nobody declared, which is the client. What a client writes to the left of its own address is never read.
   Declare a CDN by its published ranges. `KnownProxies` and `KnownIPNetworks` remain the only trust.

4. **The origin seen least often is evicted, the least recent among equals.** Scanners churn among themselves at a count
   of one, and an origin people arrive on keeps its place and its count.

5. **The startup filter surfaces what it was hiding.** It calls `KoanEnv.Initialize`, whose failure stops the boot, since
   every gate reads that snapshot. It no longer catches around `UseAuthentication` and `UseAuthorization`: `AddControllers`
   registers what they need, so a host without it stops with ASP.NET's own correction naming `AddAuthorization`.
   `UseRateLimiter`'s catch stays, because ADR-0012 makes that registration optional.

## Consequences

- Breaking: an application with a moderation or audit controller and no policies now answers 403 until it maps them. Its
  first request says what to write.
- `SocialCardSnapshot`, `CardRegistration.KeyFor` and the card lifecycle hooks are gone. An application whose resolver
  returned a row its own API would refuse now receives the default card, which is the point.
- Each matched navigation costs the resolver plus one keyed read, where it cost one snapshot read on a hit and the resolver
  on every unknown token.
- `CapabilityDefaultBehavior` orders `Deny` first, so its zero value is the safe one.

Possibilities named and not built:

- A client inside a declared network can claim any address, since its own reads as a proxy. Declaring a network says that
  network is trusted; the same held with one hop.
- A scanner that repeats each of many hosts more often than people reach a new origin can hold the record against that
  origin. It takes the limit's worth of hosts, each repeated, and the origin keeps its count once it has one.
- A selector chooses what a card says, so a property-level `[Access]` does not reach it.
- A resolver that reads a named partition finds its row absent from the reader's read, and the reader receives the
  default card. It fails closed.

## Verification

- `ReaderCardSpec` (Koan.Web.OpenGraph.Tests): a row the resolver refuses, a row that turns private, the Entity's read gate
  for anonymous and signed-in readers, its read constraint per row, a slug-routed card following an edit and a removal, and
  `Cache-Control: private` for a signed-in reader. All six failed before the change.
- `CapabilityDenyByDefaultSpec` (Koan.Web.Extensions.Tests): a signed-in caller refused on a controller with no policy and
  no call, the refusal's words, the options' default, and an application's declaration winning on either side of
  `AddKoan()`. The first three failed before the change, the first answering 200.
- `TrustedProxiesSpec` (Koan.Web.WellKnown.Tests): declared proxies in a row reach the client's address, scheme and host;
  the walk stops at the first undeclared address; a host without authorization services stops boot naming `AddAuthorization`.
- `ObservedOriginsSpec` (Koan.Web.WellKnown.Tests): a thousand random hosts leave the origin people arrive on in place with
  its count, and among equal counts the least recent goes.
- `KoanEnv.Initialize` failing at boot has no spec: the snapshot is process-static and already initialized by the time a
  test host starts.
