---
id: WEB-0089
slug: a-hook-knows-its-operation-and-a-refused-form-reads-its-page-again
domain: Web
status: Accepted
date: 2026-11-10
title: A hook knows which operation it serves, and a refused form reads its own page again
amends:
  - WEB-0087
  - WEB-0088
  - ARCH-0144
related:
  - ARCH-0143
  - ARCH-0146
---

# WEB-0089: A hook knows which operation it serves, and a refused form reads its own page again

## Context

A declared entity controller (WEB-0087) that serves a create and a change runs the same hooks for both, and `HookContext<T>` told them
no operation: it named the row as it stood (`Prior`) and the input, never which operation ran. WEB-0087 §11 told an override to hand its
hooks what it prepared in a field, so a controller kept a field per request to tell its own hooks whether they served a create, a reply
or an edit, and a hook registered as a service had no controller to ask at all. Koan itself read the action's name twice to tell a create:
for a written row's address and for the refusal of a body that cannot bind.

A refused form answered its `Problem` with what it `sent` (WEB-0088 §6) so the application's formatter could draw the form again, but a
formatter holds no page: only the controller knows the page a form was on and what that page draws. Each controller that rendered pages
matched the `Problem` after `base.Upsert` or `base.Patch` and rebuilt its page by hand, which a refusal by the gate or by binding never
reaches, since there the action never ran. ARCH-0144 §7 reserved a GET in process for the same page, a heavier road no package built.

**The business sentence:** tell my hooks what this request does, and show a refused form its own page again.

```csharp
[Route("/boards/notices")]
[Declared]
public sealed class NoticesController : EntityController<Notice>, IModelHook<Notice>
{
    [Writes(nameof(Notice.Id), nameof(Notice.Text))]
    public override Task<IActionResult> Upsert(Notice model, CancellationToken ct) => base.Upsert(model, ct);

    [HttpPatch("{notice}")]
    [Writes(nameof(Notice.Text))]
    public override Task<IActionResult> Patch(string notice, CancellationToken ct) => base.Patch(notice, ct);

    public Task OnBeforeSave(HookContext<Notice> ctx, Notice notice)
    {
        if (ctx.Operation == EntityEndpointOperationKind.Patch) notice.EditedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    protected override async ValueTask<object?> RefusedPage(EntityEndpointOperationKind operation, Problem refused, CancellationToken ct)
        => new NoticeForm(await Board.Get("general", ct), operation, refused); // the application's page; its formatter draws it
}
```

## Decision

1. **A hook is told the operation it serves.** `HookContext<T>.Operation` is the endpoint's own `EntityEndpointOperationKind`, the word
   its descriptor and its entity tools already name it by: `Collection`, `Query`, `GetNew`, `GetById`, `Upsert`, `UpsertMany`, `Patch`,
   `Delete`, `DeleteMany`, `DeleteByQuery` or `DeleteAll`, each the name of the operation a controller's author overrides and of the
   entry point that runs it; a `PUT` writes through `Upsert`. Each entry point of the entity endpoint sets its own before its first hook
   and nothing changes it after, so every hook of one request, its options, fetch, save, delete and emit steps, registered or the
   controller's own, reads the same answer, on REST and on MCP; it is `None` only where no entity operation runs. `Upsert` says what the
   route is, on a declared controller where it only creates and on an undeclared one where it creates or changes; whether a row stood is
   `Prior`'s to say. `EntityOperation` compiles the kind once per action, and what Koan asked of an action's name, a created row's address
   and whether a body that cannot bind is an upsert's or a patch's, reads it.
2. **A refused form reads the page its controller hands.** `EntityController.RefusedPage(operation, refused, ct)` is asked when a
   declared `Upsert`, `Patch` or `Delete` that came as a form is refused and its sender reads pages (`!Answers.WantsJson`), once
   `refused.Sent` holds what the form sent. What it answers is answered with the refusal's status and `Cache-Control: no-store`, for the
   application's formatter to draw; null, its default, answers the refusal as it was. `RefusedWrite` runs whether or not the action ran,
   so a refusal by the gate or by binding reads the page too, for the operation the form was sent to; where the gate refused before the
   action's controller was made, one is made to hand it. A sender that reads JSON, every tool call among them, reads the problem. Koan
   draws no page (ARCH-0143 §5): it hands the application's own object on.

## What this overturns

- WEB-0087 §11's "an override hands its hooks what it prepared in a field" for which operation runs: a hook reads that from its context.
  A field still carries what an override prepared beyond it.
- WEB-0088 §6's formatter drawing the form again from `sent` alone: it draws the page the controller hands, which is handed `sent` on
  the problem.
- ARCH-0144 §7's "a refused form's page drawn again", a GET of that page in process as the same reader: no request is made for it. The
  record serves a tool call, a read by address and the static copy.

## Consequences

- Nothing breaks: a controller that overrides nothing answers as it did, and a hook that reads no operation runs as it did.
- A refused form's page is the controller's object, answered at the refusal's status; where the controller hands none, a formatter that
  draws `Problem` keeps drawing it.
- A page handed for a gate's refusal comes from a controller made for it, whose action never ran: it reads the request, its route values
  and `refused`, and nothing an action would have bound.

## Ergonomics

New public concepts: `HookContext<T>.Operation` and `EntityController.RefusedPage`; the operation's type is the endpoint's own
`EntityEndpointOperationKind`, whose values are the names of the operations a controller overrides. A controller with a create and a
change keeps no field to tell its hooks which ran, and matches no `Problem` after `base.Upsert` or `base.Patch` to rebuild a page; a
registered hook branches on the same word, and so does an MCP tool. `RefusedPage` sits beside `AddressOf` and `KeyOf`, the controller's other answers about its
own pages and addresses.

## Coalescence

- **One list:** the operation a hook is told is the endpoint's own kind, the one `DefaultEntityEndpointDescriptorProvider` gives each of
  its operations and Koan.Mcp names its tools and gate actions from; no second list of operations exists. Each entry point of
  `EntityEndpointService` sets its own value, one for one, with no mapping, since an entity tool reaches it with no controller.
- **Per action:** `EntityOperation.Kind` names, per action, the entry point that action calls, for what is answered before or without the
  endpoint: a row's address, and a refusal by the gate or by binding. Nothing in Koan held that table: the descriptor provider lists the
  operations and their traits, keyed by nothing an action carries, and Koan.Mcp maps a kind to the endpoint call, the other way.
  `RefusedWrite` stays the one place a refused declared write is answered.
- **Deleted:** Koan's two readings of an action's name to tell a create; the in-process redraw ARCH-0144 §7 reserved, with its
  `InProcessSpec` case, whose primitive behavior, the reader carried and a value handed to the formatter, the spec's other cases keep
  proving.
- **Not built:** a flag for a replayed write (on a create's emit `Prior` already says it, and at a change's stop it would call a change
  that changed nothing a replay); an onward address a hook says (a form's `return` comes first); what a write answers in place of its row
  (`ShortCircuit` in an emit hook serves it); a typed slot for what a hook worked out; a permission bit per field of one change; a change
  that creates its row; a value folding the batch writes into the single ones, or an operation of its own for a create that only
  creates.

## Evidence

- `DeclaredControllerSpecsBase` (AdapterSurface, SQLite and in memory): each hook of a declared controller is told `Collection`,
  `GetNew`, `GetById`, `Upsert`, `Patch` and `Delete`, from the options, save, delete and emit steps; a refused form reads the page its controller gives, with the refusal's status and
  `no-store`, for a refusal in the write's hold (`409`) and one by the gate before anything bound (`401`, the row unchanged); a form
  whose binding failed reads its page with the operation it was sent to; a refused JSON write reads the problem though its controller
  hands pages, and so does a form whose page its controller declines.
- `DeclaredOperationsSpec` (Koan.Mcp.Verbs.Tests, real HTTP and real `tools/call`): a tool call tells its hooks `Upsert` and `Patch` as
  its route does, and a refused one reads the problem, while the same refusal sent as a page's form reads the controller's page.
