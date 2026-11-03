---
id: ARCH-0143
slug: one-refusal-shape
domain: Architecture
status: Accepted
date: 2026-11-03
title: A reason is a kind, a sentence and a word, a refusal is one problem document, and every page is the application's
related:
  - ARCH-0141
  - ARCH-0142
  - DATA-0167
  - SEC-0039
---

# ARCH-0143: A reason is a kind, a sentence and a word, a refusal is one problem document, and every page is the application's

## Context

A consumer that renders its own pages kept a second refusal mapping beside `Answers`, with its own problem record, an `IProblemView` adapter that negotiated each
refusal twice, and its own headings. Meanwhile `Reason` carried what only a surface knows (`Heading`, `Next`, the one absent sentence as `Said`), and a string
converted silently to an invalid reason, which needed four string operators to keep `==` from lying, and so gave `==` two meanings by static type. Koan answered
refusals in three more shapes: `CrossSiteWrites` through `Results.Problem`, `RequireCapabilityAttribute` through a `ProblemDetails` with no kind, and a plain page
of its own.

## Decision

1. **A `Reason` is its kind, its sentence and, for a gate, a stable word.** `Reason.Invalid(text)`, `Absent(text)`, `Gated(text)`, `Gated(word, text)` and
   `Conflict(text)`. Equality is the record's: a reason equals a reason of the same kind, sentence and word, and a string is never a reason. A heading above the
   sentence and the address to go to next are the surface's, because the same gate reads differently on different pages and a data layer holds no address.
2. **A refusal is one `Problem`.** `Koan.Web.Controllers.Problem` is a sealed record of `Status`, `Title`, `Detail`, `Gate` and `Errors`, and its `Kind` follows
   from its status by the one table (400, 413, 415 and 422 invalid; 401 and 403 gated; 404 absent; 409 conflict; anything else failed), so no document says a kind
   its status contradicts. Its title is its kind's name unless an application names its own. `ToResult()` answers it as `application/problem+json`.
3. **One mapping, on `Answers`.** `this.Answer(outcome, done)` as before; `this.Refused(reason)` and `Answers.Refused(http, reason)` for a reason;
   `Answers.Status(http, reason)` for a page that shows a refusal in place; `Answers.NotDone(http, outcome)` for a route that handles `Done` itself. The entry
   points are not on `Problem`, since inside a controller the name binds to `ControllerBase.Problem`. Absent says `Answers.Nothing` whatever its reason says, at
   every door: the route, the tool call that reads it back, and a custom tool's refusal.
4. **Every Koan refusal is that document:** `this.Answer`, a `[Tool]` route's gate, binding failure and size limit, the cross-site check (SEC-0039), which answers
   through MVC's formatters from middleware, and `[RequireCapability]`. `RouteToolInvoker` reads a route's kind from its document, and else from the record's
   table, which it no longer holds.
5. **Koan renders no HTML.** A refusal answers `application/problem+json` to every client, a browser included. An application that renders pages registers an
   output formatter that claims `Problem` for a request it answers with a page, as it renders any other resource; the record is sealed and plain so that the
   field-access guard leaves that choice to it. Koan's titles name the kind, and a refusal's sentence is the application's reason. Koan writes a sentence only where
   no reason exists: a gate an authority closed, a size limit, a cross-site request, and a failure, where a command that never ran says it is safe to send again.

## What this overturns

- ARCH-0141 §8's `Reason.Said`, and §9's `next`: a gated reason names no next step, and the problem carries no `next`.
- ARCH-0142 §2, that a string compared with a reason compares its text and that a plain string is a reason; and §3, its HTML negotiation, `IProblemView`, the plain
  page and `Reason.Gated(word, text, next, heading)` whose heading was the problem's title.
- DATA-0167 §3's implicit conversion of a plain string to an invalid reason: an invalid reason is `Reason.Invalid(text)`.

## Ergonomics

New public concepts: `Problem`, `Answers.Refused`, `Answers.Status`, `Answers.NotDone` and `Answers.Nothing`. Removed: `IProblemView`, `Reason.Next`,
`Reason.Heading`, `Reason.Said`, the conversion from `string` and the four string operators.

## Compatibility

- A reason declared as a string is `Reason.Invalid(...)`; the compiler finds each one, and a `const` used in a pattern becomes a membership test.
- A browser reads a refusal as `application/problem+json` until the application claims `Problem` with a formatter. The problem carries no `next`, and its title is
  its kind's name: `Gated`, not `Sign in first` or a gate's heading.
- A refusal from `[RequireCapability]` or the cross-site check now carries `kind`.
