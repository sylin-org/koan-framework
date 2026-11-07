---
id: WEB-0087
slug: a-declared-entity-controller-serves-what-it-declares
domain: Web
status: Accepted
date: 2026-11-07
title: A declared entity controller serves what it declares, and each operation declares what it writes, takes and hooks
amends:
  - ARCH-0141
  - ARCH-0146
  - ARCH-0092
related:
  - WEB-0086
  - SEC-0004
  - WEB-0073
---

# WEB-0087: A declared entity controller serves what it declares, and each operation declares what it writes, takes and hooks

## Context

`EntityController<T>` answers twelve operations whether an application wants them or not. An application that serves a Topic's
start, read and reply had no way to say that nothing else of `Post` is a route: `PUT`, bulk, query, `DELETE all` and `DELETE` came
with it, and so did `?set=`, the caller-chosen partition every operation read, so any reader could ask for `?set=audit` and read the
audit trail's removed words, and a write with `?set=x` landed where the log, the counters and the hooks never looked. An override that
stated its own route answered at the inherited one too, a gate declared on it was judged only if it was also a tool, and its address was
always the row's id, never a slug or a handle. Its write took the whole entity: record access refused every write to an entity with one
`[Access(write)]` member, and a tool's schema listed every member. Its hooks were the entity's, so two controllers over one entity ran
each other's, and nothing it took beside the entity's members, such as an audience to compile into an overwrite, could reach them.

**The business sentence:** serve these operations of this entity, at these addresses, writing these members and taking this beside them.

```csharp
public sealed record Placing([property: Description("The board it goes on.")] string? Where = null);

[Route("/boards/notices")]
[Declared]
public sealed class NoticesController : EntityController<Notice>, IModelHook<Notice>
{
    [HttpGet("{notice}")]
    [Tool(Name = "read_notice")]
    public override Task<IActionResult> GetById(string notice, CancellationToken ct) => base.GetById(notice, ct);

    [Tool(Name = "post_notice")]
    [Writes(nameof(Notice.Id), nameof(Notice.Text))]
    [Input<Placing>]
    public override Task<IActionResult> Upsert(Notice model, CancellationToken ct) => base.Upsert(model, ct);

    [HttpPatch("{notice}")]
    [Tool(Name = "edit_notice")]
    [Writes(nameof(Notice.Text))]
    public override Task<IActionResult> Patch(string notice, CancellationToken ct) => base.Patch(notice, ct);

    protected override ValueTask<string?> KeyOf(string segment, CancellationToken ct) => ...; // a slug, a handle, or the singleton's id

    public Task OnBeforeSave(HookContext<Notice> ctx, Notice notice)
    {
        if (ctx.Prior is null) notice.Board = ctx.Input<Placing>()?.Where ?? "general";
        return Task.CompletedTask;
    }
}
```

## Decision

1. **`[Declared]` is opt-in, and a declared controller serves only what it overrides.** Every operation it inherits without overriding is
   neither a route nor a tool; the entity has no `{entity}_{op}` tools there, and `[Tool]` on the class exposes its overrides as route
   tools. Undeclared controllers keep the whole surface, unchanged.
2. **An override's own HTTP attribute is its route,** in place of the one it inherits. Its route segment names the key: `[HttpGet("{topic}")]`
   binds `topic`, and the tool takes `topic`. A singleton routes its key operations at `""` (`[HttpGet("")]`, `[HttpPatch("")]`).
3. **`KeyOf(segment, ct)` maps an address to a row's id,** for `GetById`, `Patch` and `Delete` on any `EntityController`. Left alone the
   segment is the id; overridden, it answers a slug, a handle or a DID, or the one id of a singleton, which is asked with `""`. The row is
   then read by that id with every filter, as a read by id is; null answers 404.
4. **`POST ""` only creates on a declared controller.** The prior read (WEB-0086) finding a row answers `409 id-taken`, or 404 where the
   caller's read hides it, as an insert that lost a race does, and it inserts where the provider declares insert-only writes, refusing with
   501 elsewhere, as a constrained create already does.
5. **A caller-chosen partition is refused on every `EntityController`,** with `set` named, unless the controller declares the partitions
   it serves: `[Partitions("archive")]`, or `[Partitions(Any = true)]`. The entity tools of an `EntityController` that carries `[Tool]` take
   the same rule from it. A terse `[McpEntity]` and an `EntityToolset<T>` are not controllers and keep accepting one.
6. **A declared operation takes what it declares.** A query word it does not take, among them `filter`, `sort`, `page`, `with`, `shape`,
   `view`, `access` and `q`, is refused with that word named. What it takes is its declared input's members, and `set` where partitions
   are served. An input member named after one of the endpoint's own words reaches the endpoint as well, so declaring `Page` opens paging.
7. **A gate an override declares is judged before binding,** whether or not it is a tool; on a declared controller its class's gate is
   judged too, as any routed action's is (ARCH-0146). Inherited operations keep their entity's own gate.
8. **A collection or a query on a declared controller answers in the format the caller accepts,** JSON where it names none, so an emit
   hook's record reaches the application's formatter.
9. **`[Writes(...)]` names the members a declared `Upsert` or `Patch` writes.** A body that names another member is refused 400 with that
   field named, on both doors; a member it leaves out keeps its prior value on `PATCH` and its default or stamped value on `POST`. Record
   access admits each member the body names, where it otherwise admitted a replacement of the whole entity, so an `[Access(write)]`
   member a write leaves alone refuses nothing. The route tool's schema lists those members and no others.
10. **`[Input<TInput>]` declares what an operation takes beside the entity's members,** read from the query and, on a write, from the
    same body. A value that cannot be its member's type is refused with its field named; the tool's schema lists the members with their
    descriptions; a hook reads them typed with `HookContext<T>.Input<TInput>()`. An input member named like an entity member is refused
    where the application's routes are composed, because it would write the entity past its write set, and so are `[Writes]` and
    `[Input]` anywhere but a declared controller's operation, so neither is ever silently ignored.
11. **A controller is a hook of its own requests.** One that implements `IModelHook<T>`, `IRequestOptionsHook<T>`, `ICollectionHook<T>` or
    `IEmitHook<T>` runs as that hook for the requests it answers, after the registered hooks and before the access floor, on any
    `EntityController`; two controllers over one entity never run each other's. Its hook methods are not actions. A controller instance is
    per request, so an override hands its hooks what it prepared in a field. Each hook step does nothing unless a hook says otherwise, so a
    hook implements only the steps it needs.

## What this overturns

- ARCH-0141 §11 and its compatibility line: on a declared controller the operations it overrides are its own route tools, so a
  controller's overrides and `[Pagination]` reach both doors; the entity's own tools remain for an undeclared controller with `[Tool]`.
- ARCH-0146 §1's "keeps its entity's own gate" for an override that declares a gate of its own.
- ARCH-0092 §B's "same realization": a declared controller realizes only what it declares.
- SEC-0004's allow-by-default for the partition a caller names on an `EntityController`.

## Consequences

- An `EntityController` that relied on `?set=` must declare its partitions; until it does, a request naming one answers 400.
- A declared collection cannot page until it declares a paging input; a keyset cursor is its own decision.
- A declared create needs an insert-only provider (SQLite, PostgreSQL, Mongo, in memory).
- On a declared controller, the collection's own address answers 405 to a read it does not serve when it serves a create there, and 404
  everywhere nothing is served.

## Ergonomics

New public concepts: `[Declared]`, `[Partitions]`, `[Writes]`, `[Input<TInput>]`, `KeyOf`, `HookContext<T>.Input<TInput>()`,
`EntityUpsertRequest.Insert` and `Writes`, `EntityPatchRequest.Writes`, `McpEntityRegistration.Realization`, and the adapter-facing
`EntityOperation` (hidden from IntelliSense), which REST and the route tools both read. `Answers.Refused(ClaimsPrincipal?, Reason)` is
public, for a door that holds no request. Hook interfaces gain default steps and a default `Order` of 0.

## Coalescence

- **Owner:** `EntityOperation`, compiled once per action, is the one answer to what an operation serves and takes; the controller admits a
  request by it, `RouteTools` draws the schema from it, and `PartitionsAttribute.Refusal` is the one partition rule both doors ask.
- **Closest patterns:** `[Tool]` as its own model convention (ARCH-0141) for `[Declared]`; `AllowRawQuery` (WEB-0074) for a controller's
  declaration of what its query may carry; `FieldAccess`'s path admission for a write set.
- **Not built:** a sealed keyset cursor and form post-redirect-get, each its own package.

## Evidence

- `DeclaredControllerSpecsBase` (AdapterSurface, SQLite and in memory, 13 cases each): only overrides are routes (PUT, DELETE, `all`,
  bulk, query and new 404, the collection's read 405); an override's own route replaces the inherited one; a slug addresses a row through
  `KeyOf`; a singleton serves its row at `""`; a create only creates (`409 id-taken`); a create writes its declared members and its hook
  reads its input; a member outside the write set and an input that cannot be its type are refused with their fields; a change keeps
  what it leaves out and refuses a member it does not write; a gate on an override that is no tool is judged; a controller's hook runs
  for it and not for another controller over the same row; `?set=` is refused on a declared and an undeclared controller; an undeclared
  query word is refused on a declared controller and taken on an undeclared one.
- `DeclaredOperationsSpec` (Koan.Mcp.Verbs.Tests, 6 cases, real HTTP and real `tools/call`): a declared controller's tools are its
  overrides and nothing else of it; each schema is its route values, write set and input, with the input's description; a create answers
  alike on both doors with the hook reading the input; a member outside the write set is refused on both doors; a change keeps what it
  leaves out on both doors; `?set=` is refused on both doors, also for an undeclared controller's routes and the entity's own tools.
