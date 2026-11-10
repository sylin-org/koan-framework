---
id: WEB-0090
slug: a-page-reached-with-words-it-does-not-take-is-sent-on-without-them
domain: Web
status: Accepted
date: 2026-11-10
title: A page reached with words it does not take is sent on without them
amends:
  - WEB-0087
related:
  - WEB-0088
  - WEB-0089
---

# WEB-0090: A page reached with words it does not take is sent on without them

## Context

A declared operation refuses a query word it does not take, naming it (WEB-0087 §6). That is right for a client of the API, which learns
the word it mistyped, and wrong for a person following a link: a site that shares a link tags it, `fbclid` or `utm_source`, and the page
it names answered `400` where an application's own route had answered the page. Measured in Tangent on a branch that serves its pages
through declared reads, five pages answered `400` to such a link.

**The business sentence:** a link to a page opens the page, whatever another site added to it.

```http
GET /boards/notices/first-steps?utm_source=elsewhere&fbclid=IwAR0x
Accept: text/html

HTTP/1.1 302 Found
Location: /boards/notices/first-steps
```

Nothing is declared: a declared read does it.

## Decision

1. **A read of a declared address whose query names a word its operation does not take is sent on, `302`, to the same address with
   only the words it takes,** each as it was sent, a repeated or an escaped one among them, unless its sender names JSON: its `Accept`
   names JSON and not HTML. A read is a `GET`, or a `HEAD` where the operation answers one. The address is the request's own under the
   application's base, as a header carries it, and one that is not a path here, `//` another host's, is refused as before rather than
   sent on. `302`, never `301`, with nothing said of how long it holds, since the words an address takes may change; the onward query
   holds only words the operation takes, so it cannot be sent on again.
2. **What is refused stays refused.** A partition the controller does not serve is judged first and refused, `set` named. A sender that
   names JSON, every tool call among them, is refused naming the word, as WEB-0087 §6 has it. A write, and a query sent in a body, keep
   their refusals.

## What this overturns

- WEB-0087 §6's refusal of a word an operation does not take, for a read whose sender names no JSON: it is sent on without the word.

## Consequences

- A declared `GET` with a mistyped word, from a sender that names no JSON, a browser, a link preview or an API client that sends no
  `Accept`, is sent on where it was refused, and the mistyped word is dropped where it was named. A client that names JSON is told it.
- A declared read answers `HEAD` only where its override declares `[HttpHead]`, as any action does; ASP.NET routes no `HEAD` to an
  `[HttpGet]` action.

## Ergonomics

No new public concept: a declared read does it. Its cost to the reader is the one consequence above: an API client that sends no
`Accept` reads the page without the word it mistyped, rather than the word named.

## Coalescence

- **Owner:** `EntityController.Admit` keeps the one judgement of what a request names, with the partition's refusal before it; `Answers`
  builds the onward address as it builds a written row's (`Answers.Written`), through the same test of a path here and the same carriage
  of a path a header cannot carry; `Answers.WantsJson` and the read share one reading of `Accept`, the read taking only its half that
  names JSON, since a sender that names no format reads the page.
- **Not built:** dropping a word a write does not take; a list of words an application keeps through the send.

## Evidence

- `DeclaredControllerSpecsBase` (AdapterSurface, SQLite and in memory): a list and a row read with `fbclid` and `utm_source`, as
  `text/html` and as `*/*`, by `GET` and by `HEAD`, are sent on, `302`, to the address with only the words they take, a repeated `size`
  and an escaped `after` kept as sent, saying nothing of freshness; `set=audit` from a page reader is still `400` naming `set`; a word not
  taken from a sender that names JSON is still refused naming it.
- `FormWriteUnderBaseSpec` (Koan.Web.WellKnown.Tests, under `Koan:Web:ApplicationUrl` with a path): a read sent on stays under the base.
