# Sylin.Koan.Mcp

Project the entities and business tools an application already governs to AI agents over the Model
Context Protocol, under the access declarations that govern its HTTP surface.

Annotating an entity with `[McpEntity]` is the whole application-side change. Each caller then
discovers the tools and resources *that caller* may use, over STDIO or Streamable HTTP, and can read
the same runtime facts an operator sees.

## Install and shortest supported path

The generated [product surface](../../docs/reference/product-surface.md) owns support maturity and
publication truth for this package.

```powershell
dotnet add package Sylin.Koan.Mcp
```

Add MCP to the
application closure (`Sylin.Koan.Mcp` is the package identity), annotate the entity, and keep the
normal Koan bootstrap:

```csharp
using Koan.Data.Core.Model;
using Koan.Mcp;

[McpEntity(Name = "product", Description = "A product in the public catalog", AllowMutations = false)]
public sealed class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}
```

```csharp
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

The package reference is the registration: `AddKoan()` maps the protocol endpoints and owns the
host handle.

STDIO is enabled by default. To add the HTTP transport in an application that also references
Koan Web, declare the intent in configuration:

```json
{
  "Koan": {
    "Mcp": {
      "EnableStreamableHttpTransport": true,
      "Exposure": "Tools"
    }
  }
}
```

## Streamable HTTP

The default base route is `/mcp`, and there is no session: a tool call is already a fresh request ([SEC-0039](../../docs/decisions/SEC-0039-a-caller-brings-its-own-credential.md)).

| Request | Meaning |
|---|---|
| `POST /mcp` | Send one JSON-RPC message, `initialize` and tool calls alike, answered as one JSON object. |
| `GET /mcp` | A browser reaches the console when one is registered; anything else is `405`, as there is no stream, with a JSON-RPC error. |
| `DELETE /mcp` | `405`: there is no session to end. |

After `initialize`, send the negotiated `MCP-Protocol-Version`. A `Mcp-Session-Id` a client still sends
means nothing. A normal MCP client performs this negotiation—application developers do not write
JSON-RPC handlers.

A browser's `Origin` must be the application's own: the base URL in force, or one `Koan:Mcp:AllowedOrigins` names.
A request with no `Origin` is not a browser's and passes. A request the transport cannot take is answered with its
HTTP status and a JSON-RPC error with no id, whose `data` is the `Problem` ([AI-0042](../../docs/decisions/AI-0042-the-edge-reads-by-address-and-fails-in-one-shape.md)).

## Reading by address

Where an agent should read the application the way a person follows links, turn it on:

```json
{ "Koan": { "Mcp": { "ReadByAddress": true } } }
```

The tool `read` then answers what a GET of any address of the application answers, as JSON where the route answers
in kind, as its caller: it is sent through the application's whole pipeline in process (ARCH-0144), so each address
answers to its own gate as it does over HTTP, and `read` opens no door a route had not opened. An address is a path or an
absolute URL of the application; with none it reads `/`. A redirect within the application is followed once, and its
result's `meta.address` says where the answer came from. Another host's address is refused, naming the field. Every
route tool with route values also takes an `address` in their place, its query too: the address of a Topic names
where a reply to it is sent.

## Failures

Every failure at the edge has one shape, the `Problem`. A refused tool call carries it in `structuredContent` and
`meta.problem`, whether the route refused or the edge did before any request, with its `status` and its kind as
`errorCode`; `meta` also carries the route's `Retry-After` as `retryAfter`, its `WWW-Authenticate` as `challenge`,
and for a tool that writes whether anything `landed` where that is known: `false` for a refusal and for what never
ran, `true` for success. A value for a route's path or query is the type the route takes, and a wrong one is refused on
its field. A JSON-RPC error carries the `Problem` as its `data`.

## Instructions

What `initialize` tells a client is composed when it asks: register an `IMcpInstructions` (`Koan.Mcp.Hosting`) as a
singleton. With none, `Koan:Mcp:Instructions` is told, and then the application's description.

## Names

A tool's name is lower-case words joined by underscores, at most 64 characters, whichever door made it: `product_get_by_id`, `koan_code_execute`. Claude's Messages API refuses the dot MCP allows, so a name outside this rule stops the host at start (ARCH-0142).

## Governance

`AllowMutations = false` removes generated mutation tools, but it is not an authorization system.
Use Koan's entity `[Access]` declarations for data authority and tool access policies/scopes for
custom workflows. The HTTP edge has no authentication of its own: a caller brings its own credential,
which the application's authentication reads at `/mcp` as on any route, and a caller with none is a visitor
whom each tool's gate judges (SEC-0039).

Tool advertisement and enforcement share the same caller-aware projection. A denied or disabled
tool is not offered and cannot be reached by calling its name directly.

Property access uses the same Web declaration for typed Entity and custom-tool contracts:

```csharp
[Access(read: "is:admin", write: "is:admin")]
public List<string> ClaimantIds { get; set; } = [];
```

The caller's field permissions govern output, supplied input, filter/sort paths, and JSON Patch
sources and destinations. `[McpIgnore]` remains an additional exclusion even for administrators.
Server-owned Entity access predicates and ordinary Data operations retain their authority.

Whole-entity replacement refuses when a protected field cannot be written, including when that
field is omitted from the request. Use an admitted patch for individual writable fields. Conditional
fields may remain in static schemas as a superset; discovery does not grant field access.
Supplied typed custom-tool arguments are also whole values and require permission to write every
protected member before binding, including constructor-bound members.

A verb that already answers at a route needs no second definition: mark the action or its controller `[Tool]`, from `Koan.Web`, and a tool call is a request to it,
with the same input, gate, refusals and effect ([ARCH-0141](../../docs/decisions/ARCH-0141-one-verb-a-route-that-is-also-a-tool.md)). `[McpTool]` is for a verb with no route. Either answers to the one gate:
`[Access]` or `[RequireScope]` on the method or its class, and a value that cannot bind is refused with its name.

Set `IsMutation = true` on a mutating `[McpTool]`. Its input schema advertises `dry_run`; because
arbitrary imperative effects are not inspectable, `dry_run: true` returns a non-executing partial
rehearsal that names that boundary. Generated Entity mutations return framework-owned prospective
deltas. Convention-based Entity property names are valid fallback schema descriptions and do not
produce startup warnings; add `[McpDescription]` only when richer agent guidance adds value.

## Runtime inspection

- `koan://self` introduces the caller-visible Entity and custom-workflow surface.
- `koan://entities` remains Entity-specific and can honestly be empty in a custom-tool-only app.
- `koan://facts` returns the same versioned, redacted runtime-fact envelope as startup, health, and
  `/.well-known/Koan/facts`. Check `complete` and branch on stable fact codes rather than prose.

## Guarantees and boundaries

- Package reference plus `AddKoan()` is the only supported registration path. MCP service composition and endpoint
  mapping are framework internals.
- Entity tools reuse Koan Web's governed endpoint pipeline; MCP does not create a second authorization, filtering,
  relationship, or mutation implementation.
- The HTTP edge authenticates nobody itself, and `RequireAuthentication` only closes it to visitors. Declare Entity
  access and custom-tool scopes for business authority, on the entity, since a controller's `[Authorize]` does not
  reach an entity's tools. `/mcp` takes `application/json` alone, and `Koan:Web:RefuseCrossSiteWrites` is on.
- STDIO is a trusted local process-owner transport. Do not expose its streams through a remote shell or relay and
  infer HTTP-style identity guarantees.
- Code Mode executes Jint JavaScript under bounded CPU, memory, recursion, code-length, and SDK-call controls. It is
  not an operating-system sandbox and exposes only the Koan SDK bindings supplied by the package.
- Session limits, resumable stream state, and dry-run projection do not provide exactly-once effects, distributed
  session durability, or rollback for arbitrary custom-tool side effects. Sessions are process-owned; a distributed
  deployment must route a session consistently or re-initialize after it moves.
- Rejected entity mutations carry no state delta. Constrained Create uses the same atomic insertion
  boundary as REST; a dry-run previews caller-visible intent without reserving an identity or proving
  that a later insertion will succeed.
- Typed field protection does not track values copied into unannotated DTOs, prebuilt JSON, or strings.
  Unprepared governed polymorphic values and unsupported converters refuse before a typed payload is returned.
  Context-free Code Mode JSON conversion accepts projected JSON and refuses CLR contracts requiring field authority.

## Related packages

- `Koan.Core` — composition, startup reporting, and runtime facts.
- `Koan.Data.Core` — Entity persistence and query semantics.
- `Koan.Web` — governed REST and Streamable HTTP hosting.
- `Koan.Mcp.Explorer` — an optional human inspection surface over the same projection.
- `Koan.Mcp.Operations` — optional, explicitly enabled Jobs/Cache control-plane tools with grants and audit.

See [TECHNICAL.md](TECHNICAL.md) for composition and transport details.

## What it adds

Model Context Protocol integration for Koan entity endpoints: discovery, schema, execution, and transports.

## Transport options

`EnableStreamableHttpTransport` enables the Streamable HTTP edge, the one HTTP transport. No transport
is enabled implicitly by any other setting.
