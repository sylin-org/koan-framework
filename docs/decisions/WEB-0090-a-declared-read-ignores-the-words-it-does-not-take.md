---
id: WEB-0090
slug: a-declared-read-ignores-the-words-it-does-not-take
domain: Web
status: Accepted
date: 2026-11-10
title: A declared read ignores the words it does not take
amends:
  - WEB-0087
related:
  - WEB-0089
---

# WEB-0090: A declared read ignores the words it does not take

## Context

A declared operation refused a query word it does not take, naming it (WEB-0087 §6). That is right for a write, and wrong for a person
following a link: a site that shares a link tags it, `fbclid` or `utm_source`, and the page it names answered `400`.

**The business sentence:** a link to a page opens the page, whatever another site added to it.

Nothing is declared: a declared read does it.

## Decision

1. **A `GET` or `HEAD` of a declared address drops from its query every word its operation does not take,** before anything reads the
   query, for every sender, and answers as it would without them. The endpoint's own words it does not take, `filter`, `sort` or `with`,
   are dropped with the rest, so none is honoured.
2. **What is refused stays refused.** A partition the controller does not serve is judged first and refused, `set` named. Every other
   method, a write and a query sent in a body, still refuses a word it does not take, naming it.

## What this overturns

- WEB-0087 §6's refusal of a query word an operation does not take, for a read.

## Consequences

- A client of the API reading over plain HTTP is no longer told a word it mistyped on a read; the read answers as if it were not there. A
  tool call never was, since its query is built from the route's declared fields.
- The request's query holds only the taken words from there on, so what reads it after the action began, a result filter or a log
  written at the end, reads those.

## Coalescence

- **Owner:** `EntityController.Admit`, the one judgement of what a request names, after the partition's refusal. Every action of
  `EntityController` admits before it reads the query, `GetCollection` before the keyset read it hands to.
- **Deleted:** the redirect of a tagged read and its onward address.
- **Not built:** dropping a word a write does not take.

## Evidence

- `DeclaredControllerSpecsBase` (AdapterSurface, SQLite and in memory): a declared list read with `fbclid` and `utm_source` answers `200`
  with the same list as `text/html`, as `*/*` and as JSON; read with `filter`, `sort` or `with` it answers the plain list; `size` and
  `after` work beside a dropped word; `set=audit` is still `400`; a `PATCH` and a `POST` with a word they do not take are refused naming
  it.
