---
id: ARCH-0145
slug: a-reason-names-its-word-and-its-field
domain: Architecture
status: Proposed
date: 2026-11-04
title: Every reason has a word, an invalid one may name its field, and a refusal carries the word as word
amends:
  - ARCH-0143
related:
  - ARCH-0141
  - ARCH-0146
  - DATA-0167
---

# ARCH-0145: Every reason has a word, an invalid one may name its field, and a refusal carries the word as word

> **Proposed.** It amends a record accepted the day before, so it lands on the owner's word. The code on its branch carries it.

## Context

ARCH-0143 gave a gate a stable word and nothing else one. A client that must tell two refusals of one kind apart, a reused id from
a stale edit, both conflicts, could only compare sentences meant for a person. A route that refuses a value it bound kept its own
table of which reason concerns which field, so a page could show it under that field and a client learn where to look, though the
reason was declared once and could have said so. And the problem carried the word as `gate`, a name that fits one kind of the four.

## Decision

1. **Every reason has a word.** `Reason.Word` is the word the reason names, and otherwise its kind's: `invalid`, `absent`, `gated`,
   `conflict`. `Reason.Gated(word, text)` names one as before, and `Reason.Conflict(word, text)` names one for a conflict a client must
   tell apart from another. Equality stays the record's, the word and the field among its values.
2. **An invalid reason may name the wire field it concerns:** `Reason.Invalid(field, text)`, the name the field travels under, as a
   form or a JSON body sends it. `Answers.Refused` carries it as `errors[field]`, the sentence its one entry, beside `detail`.
3. **The problem carries `word`, on every refusal,** in place of `gate`, with no alias: a reason's word through `Answers`, and the kind
   where nothing names another, so Koan's own refusals (a size limit, a cross-site request, a failure, a gate's fixed sentences) carry
   their kind. `Problem.Word` is never null.
4. Everything else of ARCH-0143 stands: one `Problem`, one mapping on `Answers`, absent saying `Answers.Nothing` whatever its reason
   says, and Koan rendering no page.

## Ergonomics

New: `Reason.Invalid(field, text)`, `Reason.Conflict(word, text)`, `Reason.Field`. Changed: `Reason.Word` is never null; `Problem.Gate`
is `Problem.Word`.

## Compatibility

- A client that read the problem's `gate` reads `word`. Where it branched on a gate's word, the word is the same.
- A reader of `Reason.Word` that took null for "no gate" reads the kind's word there.
- A route that kept its own table of which reason concerns which field can declare the field on the reason.
