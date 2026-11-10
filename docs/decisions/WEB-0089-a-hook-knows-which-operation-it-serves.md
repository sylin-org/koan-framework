---
id: WEB-0089
slug: a-hook-knows-which-operation-it-serves
domain: Web
status: Accepted
date: 2026-11-10
title: A hook knows which operation it serves
amends:
  - WEB-0087
  - WEB-0088
  - ARCH-0144
related:
  - ARCH-0143
---

# WEB-0089: A hook knows which operation it serves

## Context

A declared entity controller (WEB-0087) that serves a create and a change runs the same hooks for both, and `HookContext<T>` told them
no operation: it named the row as it stood (`Prior`) and the input, never which operation ran. WEB-0087 §11 told an override to hand its
hooks what it prepared in a field, so a controller kept a field per request to tell its own hooks whether they served a create, a reply
or an edit, and a hook registered as a service had no controller to ask at all. And each controller that rendered pages matched the
`Problem` after `base.Upsert` or `base.Patch` by hand to show a refused form its page again.

**The business sentence:** tell my hooks what this request does, and show a refused form its own page again.

```csharp
public Task OnBeforeSave(HookContext<Notice> ctx, Notice notice)
{
    if (ctx.Operation == EntityEndpointOperationKind.Patch) notice.EditedAt = DateTimeOffset.UtcNow;
    return Task.CompletedTask;
}

public override async Task<IActionResult> Upsert(Notice model, CancellationToken ct)
    => await this.Kept(await base.Upsert(model, ct), async refused => new NoticeForm(await Board.Get("general", ct), refused));
```

## Decision

1. **A hook is told the operation it serves.** `HookContext<T>.Operation` is the endpoint's own `EntityEndpointOperationKind`, the word
   its descriptor and its entity tools already name it by: `Collection`, `Query`, `GetNew`, `GetById`, `Upsert`, `UpsertMany`, `Patch`,
   `Delete`, `DeleteMany`, `DeleteByQuery` or `DeleteAll`, each the name of the operation a controller's author overrides and of the
   entry point that runs it; a `PUT` writes through `Upsert`. Each entry point of the entity endpoint sets its own before its first hook
   and nothing changes it after, so every hook of one request, its options, fetch, save, delete and emit steps, registered or the
   controller's own, a related type's during `?with=` among them, reads the same answer, on REST and on MCP; it is `None` only where no
   entity operation runs. `Upsert` says what the route is, on a declared controller where it only creates and on an undeclared one where
   it creates or changes; whether a row stood is `Prior`'s to say. `EntityOperation` compiles the kind once per action, and what Koan
   asked of an action's name, a created row's address and whether a body that cannot bind is an upsert's or a patch's, reads it.
2. **A controller hands a refused form its page through `this.Kept(answer, page)`:** where a write's answer, or a verb's, is a
   `Problem`, the request came as a form and its sender reads pages, it answers the page `page` composes from that problem, which
   carries what the form sent as `Sent`, at the refusal's status and never cached; anywhere else, or where `page` composes none, the
   answer stands.

## What this overturns

- WEB-0087 §11's "an override hands its hooks what it prepared in a field" for which operation runs: a hook reads that from its context.
- WEB-0088 §6's formatter drawing the form again from `sent` alone: the action hands the page through `Kept`, and the formatter draws it.
- ARCH-0144 §7's "a refused form's page drawn again", a GET of that page in process as the same reader: no request is made for it.

## Consequences

- Nothing breaks: a hook that reads no operation runs as it did, and a controller that calls no `Kept` answers as it did.
- A refusal by a gate or by binding, where no action ran, answers the refusal, with what the form sent (WEB-0088 §6).

## Ergonomics

New public concepts: `HookContext<T>.Operation`, of the endpoint's own `EntityEndpointOperationKind`, and `Answers.Kept`, beside
`Answers.Written`. A controller with a create and a change keeps no field to tell its hooks which ran, and matches no `Problem` to draw a
page; a registered hook branches on the same word, and so does an MCP tool.

## Coalescence

- **One list:** the operation a hook is told is the endpoint's own kind, the one `DefaultEntityEndpointDescriptorProvider` gives each of
  its operations and Koan.Mcp names its tools and gate actions from; no second list of operations exists. Each entry point of
  `EntityEndpointService` sets its own value, one for one, with no mapping. `EntityOperation.Kind` names, per action, the entry point it
  calls; nothing in Koan held that table, since the descriptor provider lists the operations keyed by nothing an action carries.
- **Deleted:** Koan's two readings of an action's name to tell a create; the in-process redraw ARCH-0144 §7 reserved, with its
  `InProcessSpec` case.
- **Not built:** a flag for a replayed write; an onward address a hook says; a typed slot for what a hook worked out; a hook that draws a
  refused form's page where no action ran, since a gate that refused found nothing a page would read.

## Evidence

- `DeclaredControllerSpecsBase` (AdapterSurface, SQLite and in memory): each hook of a declared controller is told `Collection`,
  `GetNew`, `GetById`, `Upsert`, `Patch` and `Delete`, from the options, save, delete and emit steps; a refused form reads the page its
  action hands through `Kept`, with the refusal's status, `no-store` and what it sent, a JSON sender reads the problem, and where the
  action composes no page the refusal answers as it is.
- `RelationshipExpansionVisibilitySpecs` (AdapterSurface, in memory): the related type's options hook, run for a maker's read with
  `?with=all`, is told `GetById`.
- `DeclaredOperationsSpec` (Koan.Mcp.Verbs.Tests, real `tools/call`): a tool call tells its hooks `Upsert` and `Patch`, as its route does.
