---
id: AI-0042
slug: the-edge-reads-by-address-and-fails-in-one-shape
domain: AI
status: Accepted
date: 2026-11-04
title: The MCP edge reads any address as its caller, takes an address in place of route values, fails in one shape, and tells what the application composes
amends:
  - ARCH-0141
  - AI-0037
related:
  - ARCH-0143
  - ARCH-0144
  - SEC-0039
---

# AI-0042: The MCP edge reads any address as its caller, takes an address in place of route values, fails in one shape, and tells what the application composes

## Context

An agent at the MCP edge met an application through one named tool per route it was allowed to read (ARCH-0141), while
the application's pages already answer JSON at the addresses they print. An agent holding an address it had just read
had to take it apart into route values to read or act on it again. A live walk of a consumer at `/mcp` found the rest:

- **The edge failed in four shapes.** A route's refusal carried its `Problem`; a refusal the edge made before any request
  (a missing route value, arguments past the route's limit, a dot segment, a failed route) carried none; a transport
  refusal answered `{error, message}`, neither JSON-RPC nor a `Problem`; and a JSON-RPC error said only its code.
- **A route value of the wrong type was read as a string.** `topic: 5` became `"5"` and `{}` became its JSON text, so an
  agent's mistake answered as absent instead of naming its field.
- **What a refusal knew stayed in the nested response.** `/mcp` answers a tool's `401` inside a `200` JSON-RPC result, and
  the invoker kept only the status, the body and `Location`: a route's `WWW-Authenticate` and `Retry-After` never reached
  the agent, which could not tell a write that never ran from one whose outcome is unknown.
- **`Origin` was checked only against a configured list**, which nothing sets, so the DNS-rebinding guard the
  specification requires was off.
- **`initialize`'s instructions were one string in configuration**, so an application could not say what it is now, or
  where it answers, without repeating its own facts in a setting that drifts from them.

**The business sentence:** let an agent read this application at any address, as itself, and tell it plainly when it
cannot.

```json
{ "Koan": { "Mcp": { "ReadByAddress": true } } }
```

```csharp
public sealed class PlaceInstructions : IMcpInstructions
{
    public Task<string?> Instructions(CancellationToken ct) => Task.FromResult<string?>("Read with read(address); start at the front page.");
}
// services.AddSingleton<IMcpInstructions, PlaceInstructions>();
```

The setting and the provider are the whole surface. The failure shape, typed route values and the `Origin` guard need
nothing.

## Decision

1. **`read(address)` answers what a GET of an address of the application answers,** as JSON where the route answers in
   kind, sent through the application's whole pipeline in process as the caller of the request in hand (ARCH-0144 rule
   4). It is offered where `Koan:Mcp:ReadByAddress` is on. Every address answers to its own route's gate as over HTTP, so
   `read` opens no door a route had not opened, is listed to every caller and asks no gate of its own, and its annotations
   say it reads, changes nothing and reaches nothing outside the application. With no address it reads `/`.
2. **An address of the application** is a path under the base URL, or an absolute URL whose origin is the base URL in
   force or the one the call arrived on, under its path (`Addresses`). Its dot segments are resolved as HTTP resolves
   them and its fragment dropped. Any other host's address, and a scheme-relative one naming another host, is refused as
   `invalid`, naming the field `address`. With no request in hand and no base URL, only a path is an address.
3. **A redirect within the application is followed once,** resolved against the address that answered it, and the result's
   `meta.address` says where the answer came from. A redirect elsewhere, and a second one, are answered as they are, with
   their status and `location`.
4. **A route tool with route values takes an `address` in their place,** where reading by address is on. The address is
   matched against the route from its start, a literal by itself, a value taking one segment and a catch-all the rest;
   what the route has past the address may be only its own words or values it can do without, so the address of a Topic
   names where a reply to it is sent. The address's query fills the route's query fields left unnamed. A value named
   beside the address must agree with it, or is refused on its field, and an address the route cannot take is refused on
   `address`. The route's values are then not required on their own in the schema.
5. **Every failure at the edge carries a `Problem`** (`ToolAnswers`). A failed tool call carries it in `structuredContent`
   and `meta.problem`: the route's own, ASP.NET's own given the kind its status means, or one the edge words for what it
   refused before any request. `meta` says the status and the kind as `errorCode`; an entity tool and code mode keep their
   own codes and carry the `Problem` their code stands for. A JSON-RPC error carries the `Problem` as its `data`.
6. **What a refusal knew reaches the agent:** the route's `Retry-After` as `meta.retryAfter`, its `WWW-Authenticate` as
   `meta.challenge`, and for a tool that writes whether anything `landed`: `true` on success, `false` for a refusal and for
   what never ran (`503`), and nothing where the edge cannot know (any other `5xx`).
7. **A value for a route's path or query is the type the route takes,** by the schema the route's parameter gives:
   a string, a whole number, a number or true or false, and one value where the route takes one. A wrong one is refused on
   its field and never read as a string.
8. **A refusal of the transport is its HTTP status and a JSON-RPC error with no id,** as the specification has a server
   answer what it cannot accept: `-32700` for a body that is not JSON, `-32600` for one that is not a request, and `-32000`
   for every other refusal of the transport (`406`, `415`, an unsupported protocol version, HTTPS required, the `Origin`
   guard, a `GET` with no stream), as the specification's reference SDKs answer them.
9. **A browser's `Origin` is held to the application's own:** one `Koan:Mcp:AllowedOrigins` names, else the base URL in
   force, else, before the application has an address, the request's own. A request with no `Origin` is not a browser's,
   so not the rebinding threat, and passes.
10. **`initialize` tells what the application composes when asked:** its `IMcpInstructions`, else `Koan:Mcp:Instructions`,
    else its description (`McpInstructions`), and the console's surface shows the same.

## What this overturns

- ARCH-0141 rule 10's result for a refusal the edge made before any request, which carried no `Problem`, and its
  `Answered ...` text for a body that is not text, which is now said as what it is.
- ARCH-0141 rule 11's "only a route can be a tool": `read` is a tool of the edge that reaches every GET route, each
  answering to its own gate.
- AI-0037's transport refusals as `{error, message}`, and its `Origin` check against a configured list alone.
- The coercion of a route value of the wrong type to a string.

## Coalescence

- **Closest pattern:** `RouteToolInvoker`, the edge's one maker of a request, over `KoanWebPipeline.Send` (ARCH-0144),
  which names reading by address among its four uses.
- **Owner:** `Koan.Mcp`. `read` is an `McpCustomTool` marked `Reads`, so the list, `koan://self`, the console and the start
  check carry it with no new code; `AddressReader` sends it; `Addresses` is the one rule of what an address of the
  application is, which `read` and `RouteToolInvoker` share; `ToolAnswers` is the one mapping of an answer to a result,
  which the route tools, `read`, the custom verbs and the handler's own refusals use. `Koan.Web` changes not at all: it
  already answers the request, and its `Problem` is the document carried.
- **Not a second copy:** the instructions setting stays as what is told where an application registers no provider, so
  one rule (`McpInstructions`) decides it for the handshake and the console.
- **Deleted:** `RouteToolInvoker`'s own result and refusal mapping, `McpRpcHandler.Refused` without a `Problem`, and the
  transport's `WriteStatus`.

## Ergonomics

- **Expression:** one setting, `ReadByAddress`, and for composed instructions one interface with one method.
- **Guarantee and correction:** an address answers as it answers that caller over HTTP; anything the edge cannot take
  is a `Problem` naming its field or its status, never an empty or prose-only answer.
- **Public concepts:** `read`, `address`, `IMcpInstructions`. `meta`'s `problem`, `retryAfter`, `challenge`, `landed`
  and `address` are fields of the result a client already reads.
- **Cognitive branches:** one setting, off by default.

## Compatibility

- A client reading the transport's `{error, message}` now reads a JSON-RPC error whose `data` is the `Problem`.
- A route value or query value of the wrong type is refused where it was read as a string.
- A refused tool call that carried no `structuredContent` now carries the `Problem`, and an entity tool's or code mode's
  failure carries `meta.status` and `meta.problem` beside its own `errorCode`.
- A browser page on another origin is refused at `/mcp` unless `Koan:Mcp:AllowedOrigins` names it.
- With `ReadByAddress` on, a route tool's route values leave its schema's `required`, and an application with a tool
  named `read` stops at start, as two tools of one name do.

## Evidence

- `ReadByAddressSpec` (`Koan.Mcp.Verbs.Tests`, 11, real `tools/call` over Streamable HTTP to a real host): the front page as
  the caller; an absolute address and a path read as whoever calls, a visitor as a visitor; a redirect within followed
  once with its `meta.address`, a second one and one elsewhere answered as they are; another host's address and a
  scheme-relative one refused on `address`; a sign-in refusal carrying its `Problem` and the route's challenge; an address
  that names nothing answering absent, and a dot segment resolved; `reply` taking a Topic's address and a census its
  query, a contradicting value refused on its field, an address the route cannot take refused on `address`; a write that
  never ran saying `retryAfter` and `landed: false`, one that failed downstream saying nothing of what landed; `read`
  listed to everyone as a read; and `initialize` telling what the provider composes over the setting.
- `OneShapeSpec` (9): a missing route value and a dot segment refused with a `Problem`; a path value of the wrong type
  (a number, a boolean, an object) and a query value refused on their fields; a route's own refusal kept; a body that is
  not JSON, one that is not a request, a form and a `GET` answered with their status and a JSON-RPC error carrying the
  `Problem`; an unknown tool as `-32602` with an absent `Problem`; a foreign `Origin` refused, the application's own and
  none passing.
- The existing specs, in place: `Koan.Mcp.Verbs.Tests` (164 with these), `Koan.Mcp.Conformance.Tests` (166),
  `Koan.Mcp.Streamable.IntegrationTests` (25), `Koan.Mcp.Operations.IntegrationTests` (5),
  `Koan.Mcp.RelationshipVisibility.Tests` (2), `Koan.Samples.McpCodeMode.Tests` (27), `Koan.Mcp.CustomTools.Tests` (3).
  `CrossSiteWritesSpec` now sends a foreign `Origin` only where the `Origin` guard is meant to refuse it, and its fixture
  with the cross-site check off names its front end's origin.

## Later

- Before an application has an address, `Origin` is judged against the request's own, which a rebinding page also
  presents; an application that must be safe from rebinding before it has one names its origins.
- An address under a path base given as a path is taken as under the base already; one that repeats the path base names
  another address.
- A body that is not text is described, not carried; a tool result's image or resource content would carry it.
