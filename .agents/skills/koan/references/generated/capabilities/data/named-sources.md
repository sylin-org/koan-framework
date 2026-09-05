---
type: REFERENCE
domain: data
title: "Named publication source"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-27
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-27
  status: passed
  scope: cold-executed by an external agent on the SQLite path (two physically distinct stores) against published packages - scoped publish identity-stable across re-runs, default untouched, absent source fails closed with "Source 'Absent' is not configured"
---

# Named publication source

Publish an approved Entity to a separately configured audience while ordinary reads and writes keep
using the application default.

## You need

| Piece | Package | Note |
|---|---|---|
| Working store | the existing Data connector | remains the default editorial or operational source |
| Named publication store | any suitable referenced Data connector | may use the same engine or a different one |
| Scoped routing | no extra package | `EntityContext.Source("Published")` makes the exceptional write explicit |

## The constraint box

> **The constraint:** A named source is a second audience, not a cutover and not a status field. It
> does not copy old rows, mirror later changes, withdraw records, or make two stores transactional.
> Publishing must be identity-stable, re-runnable, and visibly fail if the named source is absent.

## Do not confuse these outcomes

| Outcome | Route |
|---|---|
| Draft and published are statuses for the same audience | one store and a status field |
| Readers must see only an approved copy | `EntityContext.Source("Published")` around publish/withdraw |
| Replace the application's live default | [verified store cutover](store-cutover.md) |
| Publish a large approved batch | named source inside a [background Job](../work/background-jobs.md) |

## Leaves

- **Pasteable, source-verified build:** [publish to a named channel](../../recipes/publish-to-a-named-channel.md)
- **Runnable exemplar:** [DevPortal](https://github.com/sylin-org/koan-framework/blob/main/samples/applications/DevPortal/README.md)
- **Context grammar:** [Entity capability hooks](entities.md#context-hooks)
