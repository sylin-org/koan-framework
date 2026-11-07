---
id: WEB-0088
slug: a-declared-entity-write-takes-a-form-and-answers-once
domain: Web
status: Accepted
date: 2026-11-07
title: A declared entity write takes a page's form, and every write answers once
amends:
  - WEB-0087
related:
  - WEB-0086
  - SEC-0039
  - ARCH-0143
---

# WEB-0088: A declared entity write takes a page's form, and every write answers once

## Context

A server-rendered application's pages post forms: `application/x-www-form-urlencoded`, by `POST` alone, expecting to be sent on to a
page (post, redirect, get), and expecting a refusal to show the form again as it was filled. A declared `EntityController` (WEB-0087)
took JSON alone: a form create answered `415`, a change could not be asked for by a browser at all, and a committed write answered `200`
with the row whatever the sender was, with no `Location`, so a page had to hand-write its own `303`, its own safe `return` field, its own
"does this sender read JSON" test and its own `201`. An application's verbs beside the entity, which send their own commands, answered
the same way by hand, once per route.

**The business sentence:** a page and an agent write through the same operation, and each is answered as it reads.

```csharp
[Route("/boards/{board}/notices")]
[Declared]
public sealed class NoticesController : EntityController<Notice>
{
    [Writes(nameof(Notice.Id), nameof(Notice.Text))]
    public override Task<IActionResult> Upsert(Notice model, CancellationToken ct) => base.Upsert(model, ct);

    [HttpPatch("{notice}")]
    [Writes(nameof(Notice.Text))]
    public override Task<IActionResult> Patch(string notice, CancellationToken ct) => base.Patch(notice, ct);

    protected override string AddressOf(Notice row) => $"/boards/{Uri.EscapeDataString(row.Board)}/notices/{Uri.EscapeDataString(row.Slug)}";
}

[Route("/boards/{board}/notices/{notice}")]
public sealed class NoticeVerbsController : ControllerBase
{
    [HttpPost("pin")]
    public async Task<IActionResult> Pin(string board, string notice, CancellationToken ct)
        => this.Answer(await new PinNotice(notice).Send(ct), pinned => this.Written($"/boards/{board}/notices/{notice}", pinned, created: false));
}
```

```html
<form method="post" action="/boards/general/notices/first-steps">
  <input type="hidden" name="_method" value="PATCH">
  <input type="hidden" name="return" value="/boards/general">
  <textarea name="text"></textarea>
</form>
```

## Decision

1. **`Answers.Written(controller, location, body, created)` is the one answer to a write that committed,** for an entity write and for
   any action. It is never cached (`Cache-Control: no-store`). A sender that reads pages is sent on with `303`: to the path its form's
   `return` field names under the application's base, where that is one slash and a path — never `//`, a backslash or a control
   character — and otherwise to `location`. A sender that reads JSON reads `body`, `201` where the write created it and `200` otherwise,
   with `location` as its `Location`.
2. **`Answers.WantsJson(request)` says who reads JSON:** a JSON body; an `Accept` naming JSON and not HTML; or no form and an `Accept`
   naming no HTML. A browser's form, and anything asking for HTML, reads pages, so a request with no body and no preference, as an API
   client's `DELETE` is, keeps reading JSON.
3. **A declared write takes a page's form as well as JSON.** `Upsert`, `Patch` and `Delete` read a urlencoded or multipart form as the
   JSON object of its fields, each field typed as the entity member or the declared input member it names, an empty one being no value
   where the member is not text. The write set, the input, record access and every refusal of WEB-0087 then judge it as they judge the
   JSON, on one path: a field outside the write set is refused naming it, and a value that cannot be its member is refused naming its
   field. `_method` and `return` are the framework's fields, not the write's. An undeclared controller takes the bodies it always took.
4. **A form names the method it cannot send.** Where the host maps a declared `EntityController`, a urlencoded `POST` whose `_method`
   field says `PATCH` or `DELETE` is routed as that method. It is read before routing and after the cross-site check
   (SEC-0039), which judged it as the `POST` it arrived as; a multipart form is never read there, so an upload reaches its endpoint and
   that endpoint's own limits unread, and any other value changes nothing.
5. **A declared write answers through `Answers.Written`.** A create at the row's address, `201`; a replay its hook answers, as the
   create was; a change at the row's address, `200`; a delete at the address the row was listed under. `AddressOf(row)`, a path under
   the base, names the row's address: left alone it is the address the write was sent to where that named the row, and that address
   followed by the row's id for a create. An undeclared controller answers as it always did.
6. **A refused form keeps what it sent.** A declared write that came as a form and is refused answers its `Problem` with what the form
   sent as `sent`, each field with its values, so the application's formatter renders the form again as it was filled, with the
   refusal's reason inline. A body that cannot bind is that same problem, naming each field it could not take.

## Consequences

- A declared create answers `201`, with `Location`, where it answered `200`; on MCP its route tool reads the same status.
- A urlencoded `POST` on a host with a declared controller is read before routing, within ASP.NET's form limits rather than an
  endpoint's own `[RequestSizeLimit]`; an endpoint that must bound a urlencoded body more tightly than that reads it as multipart.
- `AddressOf`'s default suits a controller whose rows answer at its own collection's address by id; one whose rows answer by slug, or
  at a route of their own, overrides it.

## Ergonomics

New public concepts: `Answers.Written`, `Answers.WantsJson` made public, `EntityController.AddressOf`, `Problem.Sent`, and the form
field names `KoanWebConstants.Form.Method` (`_method`) and `.Return` (`return`). Nothing is declared to take a form: a declared write
takes one.

## Coalescence

- **Owner:** `Answers` owns the answer to a committed write as it owns the refusal; `SentForm` is the one reading of a form as the
  members it names, which the input formatter and the controller both use; `FormMethod` is the one method override.
- **Closest patterns:** `Answers.Refused` for a write's one answer; the JSON input formatter, wrapped for field access, for binding a
  form; ASP.NET's `HttpMethodOverrideMiddleware`, narrowed to urlencoded forms and the two methods a declared write takes a form for.
- **Not built:** a form that writes a nested object or a file; an undeclared controller's form writes.

## Evidence

- `DeclaredControllerSpecsBase` (AdapterSurface, SQLite and in memory, 21 cases each): a form create is sent on (`303`) to the row's
  address, never cached; a form is sent on to its `return` where that is a path here and to the row otherwise (`//host`, an absolute
  URL, a backslash, a relative path); a form that reads JSON reads the row (`201`), and one that also reads pages is sent on; `_method=PATCH`
  reaches the change; a form delete takes its input from the form, refuses with what it sent, and is sent on to the list; a refused form
  keeps what it sent, for a member outside the write set and for a value that cannot be its member, and a refused JSON write carries no
  `sent`; a verb answers through `Answers.Written` alike; a JSON create answers `201` at its address and a replay answers as it did.
- `DeclaredOperationsSpec` (Koan.Mcp.Verbs.Tests): a create answers `201` alike on REST and on `tools/call`.
